using System.Text.RegularExpressions;
using AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.application.endpointGroups;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.domain.model.@enum;
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
                var windowEnd = req.WindowStart.AddSeconds(entry.ActiveSeconds);

                var existing = await dbContext.ActivityHistories
                    .Where(h => h.UserId == userId && h.ActivityId == activityId && h.EndTimestamp == req.WindowStart)
                    .FirstOrDefaultAsync(ct);

                if (existing != null)
                {
                    existing.EndTimestamp = windowEnd;
                    existing.Length = new MyIntTime(existing.Length.TotalSeconds + entry.ActiveSeconds);
                }
                else
                {
                    dbContext.ActivityHistories.Add(new ActivityHistory
                    {
                        UserId = userId,
                        ActivityId = activityId,
                        StartTimestamp = req.WindowStart,
                        EndTimestamp = windowEnd,
                        Length = new MyIntTime(entry.ActiveSeconds),
                    });
                }
            }
        }

        await dbContext.SaveChangesAsync(ct);

        await SendAsync(processedCount, StatusCodes.Status201Created, ct);
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