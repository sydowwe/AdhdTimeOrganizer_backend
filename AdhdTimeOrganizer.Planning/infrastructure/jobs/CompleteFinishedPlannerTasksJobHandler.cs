using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.Planning.application.helper;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using FastEndpoints;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.Contracts.user;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Planning.infrastructure.jobs;

/// <summary>
/// Completes every <see cref="PlannerTask.AutoCompleteAtEnd"/> task that is <c>InProgress</c> and whose slot has
/// ended on its owner's clock. Today that is rest booked with the leisure picker's "do it now": rest does not need
/// a tick to have happened, and without this the block stayed <c>InProgress</c> for good — holding the home
/// now-bar for the rest of the day and landing in the plan-vs-actual trend as "never happened".
/// <para>
/// <b>Why a sweep every five minutes rather than a one-shot trigger per task.</b> A per-task trigger would have
/// to be re-registered on every boot (the Quartz store is in-memory — see <c>TimerBoundaryAlarmRegistrar</c>) and
/// moved whenever the task is. A sweep needs neither, and five minutes late is no worse than the user forgetting.
/// </para>
/// <para>
/// Writes follow <c>PatchPlannerTaskStatusEndpoint</c>: <see cref="PlannerTask.ApplyStatus"/>, the reminder
/// re-sync, and the completion fan-out for a task on the user's today. <c>NotStarted</c> tasks are never closed —
/// nothing said they began.
/// </para>
/// </summary>
public class CompleteFinishedPlannerTasksJobHandler(
    DbContext dbContext,
    IReminderRegistrationService reminders,
    IUserTimeZoneReader timeZoneReader,
    TimeProvider timeProvider,
    ILogger<CompleteFinishedPlannerTasksJobHandler> logger) : IScheduledJobHandler, IScopedService
{
    public const string HandlerKey = "Planning.CompleteFinishedTasks";

    public string Key => HandlerKey;

    public async Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct)
    {
        // Unauthenticated, so the per-user query filter is inert and every user is in scope — which is the point.
        // Served by the partial index on (auto_complete_at_end, status = InProgress).
        var candidates = await dbContext.Set<PlannerTask>()
            .Include(t => t.Calendar)
            .Where(t => t.AutoCompleteAtEnd && t.Status == PlannerTaskStatus.InProgress)
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return;

        var zones = await timeZoneReader.GetTimeZonesAsync(candidates.Select(t => t.UserId).Distinct().ToList(), ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var finished = new List<(PlannerTask Task, TimeZoneInfo Zone)>();
        foreach (var task in candidates)
        {
            var zone = zones.GetValueOrDefault(task.UserId, TimeZoneInfo.Utc);
            if (EndsAtUtc(task, zone) > now)
                continue;

            task.ApplyStatus(PlannerTaskStatus.Completed);
            task.ActualStartTime ??= task.StartTime;
            task.ActualEndTime = task.EndTime;
            finished.Add((task, zone));
        }

        if (finished.Count == 0)
            return;

        await dbContext.SaveChangesAsync(ct);
        await reminders.SyncForPlannerTasksAsync(finished.Select(f => f.Task.Id).ToList(), ct);

        // The same "only on the user's today" rule PatchPlannerTaskStatusEndpoint applies before fanning out: the
        // linked to-do and routine items are about today, and a block closed just after midnight is yesterday's.
        foreach (var (task, zone) in finished.Where(f => f.Task.Calendar.Date == TaskPlannerHelper.TodayIn(f.Zone)))
            await new PlannerTaskIsDoneChangedEvent(task.ActivityId, task.UserId, true, task.TodolistItemId)
                .PublishAsync(Mode.WaitForAll, ct);

        logger.LogInformation("Auto-completed {Count} finished planner task(s)", finished.Count);
    }

    /// <summary>
    /// The instant the task's slot ends, on its owner's clock. An end at or before the start wrapped past midnight
    /// onto the next day — the convention every planner span follows.
    /// </summary>
    private static DateTime EndsAtUtc(PlannerTask task, TimeZoneInfo zone)
    {
        var endDate = task.EndTime <= task.StartTime ? task.Calendar.Date.AddDays(1) : task.Calendar.Date;
        return WallClockZone.ToUtc(endDate, task.EndTime, zone);
    }
}
