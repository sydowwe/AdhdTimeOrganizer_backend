using System.Text.RegularExpressions;
using AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.application.endpointGroups;
using AdhdTimeOrganizer.application.@event;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.domain.service;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.desktop.command;

public class DesktopActivityHeartbeatEndpoint(AppDbContext dbContext) : Endpoint<DesktopActivityWindowDto>
{
    public override void Configure()
    {
        Post("/heartbeat");
        Validator<DesktopActivityHeartbeatValidator>();
        Group<ActivityTrackingDesktopGroup>();
    }

    public override async Task HandleAsync(DesktopActivityWindowDto req, CancellationToken ct)
    {
        var userId = User.GetId();
        var processedCount = 0;
        var activitySecondsInBatch = new Dictionary<long, int>();

        var mappings = await dbContext.TrackerDesktopMappingByPattern
            .Where(m => m.UserId == userId && m.IsActive)
            .ToListAsync(ct);

        foreach (var entry in req.Entries.Where(e => e.ActiveSeconds != 0 || e.BackgroundSeconds != 0))
        {
            var match = mappings.FirstOrDefault(m => MatchesPattern(m, entry));

            if (match?.IsIgnored == true)
                continue;

            var record = new DesktopActivityEntry
            {
                UserId = userId,
                RecordDate = DateOnly.FromDateTime(req.WindowStart),
                WindowStart = req.WindowStart,
                ProcessName = entry.ProcessName,
                ProductName = entry.ProductName,
                WindowTitle = entry.WindowTitle,
                ExecutablePath = entry.ExecutablePath,
                IsFullscreen = entry.IsFullscreen,
                ActiveSeconds = entry.ActiveSeconds,
                BackgroundSeconds = entry.BackgroundSeconds,
                IsPlayingSound = entry.IsPlayingSound,
                ActiveMonitor = entry.ActiveMonitor,
            };

            dbContext.DesktopActivityEntries.Add(record);
            processedCount++;

            if (match?.ActivityId is { } activityId)
            {
                activitySecondsInBatch[activityId] = activitySecondsInBatch.GetValueOrDefault(activityId) + entry.ActiveSeconds;

                var windowEnd = req.WindowStart.AddSeconds(entry.ActiveSeconds);

                var existing = await dbContext.ActivityHistories
                    .Where(h => h.UserId == userId && h.ActivityId == activityId && h.EndTimestamp == req.WindowStart)
                    .FirstOrDefaultAsync(ct);

                if (existing != null)
                {
                    existing.EndTimestamp = windowEnd;
                    existing.Length = new IntTime(existing.Length.TotalSeconds + entry.ActiveSeconds);
                }
                else
                {
                    dbContext.ActivityHistories.Add(new ActivityHistory
                    {
                        UserId = userId,
                        ActivityId = activityId,
                        StartTimestamp = req.WindowStart,
                        EndTimestamp = windowEnd,
                        Length = new IntTime(entry.ActiveSeconds),
                    });
                }
            }
        }

        await dbContext.SaveChangesAsync(ct);

        await AutomateActivityStatusAsync(userId, req.WindowStart, activitySecondsInBatch, ct);

        await Send.ResponseAsync(processedCount, StatusCodes.Status201Created, ct);
    }

    private async Task AutomateActivityStatusAsync(long userId, DateTime windowStart, Dictionary<long, int> activitySecondsInBatch, CancellationToken ct)
    {
        if (activitySecondsInBatch.Count == 0)
            return;

        var today = DateOnly.FromDateTime(windowStart);
        var todayStart = today.ToDateTime(TimeOnly.MinValue);
        var todayEnd = today.ToDateTime(TimeOnly.MaxValue);

        foreach (var (activityId, _) in activitySecondsInBatch)
        {
            var histories = await dbContext.ActivityHistories
                .Where(h => h.UserId == userId && h.ActivityId == activityId
                         && h.StartTimestamp >= todayStart && h.StartTimestamp <= todayEnd)
                .ToListAsync(ct);

            var totalSecondsToday = histories.Sum(h => h.Length.TotalSeconds);

            var plannerTask = await dbContext.PlannerTasks
                .Include(pt => pt.Calendar)
                .Where(pt => pt.UserId == userId && pt.ActivityId == activityId
                          && pt.Calendar.Date == today
                          && pt.Status != PlannerTaskStatus.Completed
                          && pt.Status != PlannerTaskStatus.Cancelled)
                .FirstOrDefaultAsync(ct);

            if (plannerTask != null)
            {
                var durationSeconds = (int)(plannerTask.EndTime - plannerTask.StartTime).TotalSeconds;
                var wasCompleted = durationSeconds > 0 && totalSecondsToday >= durationSeconds;

                plannerTask.Status = wasCompleted ? PlannerTaskStatus.Completed : PlannerTaskStatus.InProgress;
                await dbContext.SaveChangesAsync(ct);

                if (wasCompleted)
                {
                    await new PlannerTaskIsDoneChangedEvent(activityId, userId, true, plannerTask.TodolistItemId)
                        .PublishAsync(Mode.WaitForAll, ct);
                }
            }
            else
            {
                await AutomateWithoutPlannerTaskAsync(userId, activityId, totalSecondsToday, ct);
            }
        }
    }

    private async Task AutomateWithoutPlannerTaskAsync(long userId, long activityId, int totalSecondsToday, CancellationToken ct)
    {
        var todoItem = await dbContext.TodoListItems
            .FirstOrDefaultAsync(i => i.UserId == userId && i.ActivityId == activityId
                                   && !i.IsDone && i.SuggestedTime != null, ct);

        if (todoItem != null && totalSecondsToday >= todoItem.SuggestedTime!.TotalSeconds)
        {
            todoItem.IsDone = true;
            if (todoItem.TotalCount.HasValue)
                todoItem.DoneCount = todoItem.TotalCount;
            await dbContext.SaveChangesAsync(ct);
        }

        var routineItem = await dbContext.RoutineTodoLists
            .FirstOrDefaultAsync(r => r.UserId == userId && r.ActivityId == activityId
                                   && !r.IsDone && r.SuggestedTime != null, ct);

        if (routineItem != null && totalSecondsToday >= routineItem.SuggestedTime!.TotalSeconds)
        {
            routineItem.IsDone = true;
            if (routineItem.TotalCount.HasValue)
                routineItem.DoneCount = routineItem.TotalCount;
            RoutineResetService.UpdateItemStreak(routineItem, DateTime.UtcNow);
            await dbContext.SaveChangesAsync(ct);
        }
    }

    private static bool MatchesPattern(TrackerDesktopMappingByPattern mapping, DesktopActivityEntryDto entry)
    {
        if (mapping.ProcessName != null && mapping.ProcessNameMatchType != null)
            if (!MatchesString(entry.ProcessName, mapping.ProcessName, mapping.ProcessNameMatchType.Value))
                return false;

        if (mapping.ProductName != null && mapping.ProductNameMatchType != null)
            if (!MatchesString(entry.ProductName, mapping.ProductName, mapping.ProductNameMatchType.Value))
                return false;

        if (mapping.WindowTitle != null && mapping.WindowTitleMatchType != null)
            if (!MatchesString(entry.WindowTitle, mapping.WindowTitle, mapping.WindowTitleMatchType.Value))
                return false;

        return true;
    }

    private static bool MatchesString(string? value, string pattern, PatternMatchType matchType)
    {
        if (value == null) return false;
        return matchType switch
        {
            PatternMatchType.Exact => string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase),
            PatternMatchType.Contains => value.Contains(pattern, StringComparison.OrdinalIgnoreCase),
            PatternMatchType.Wildcard => Regex.IsMatch(value, "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase),
            PatternMatchType.Regex => Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase),
            _ => false
        };
    }
}