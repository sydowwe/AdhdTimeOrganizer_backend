using System.Text.RegularExpressions;
using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.infrastructure.security;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.Tracking.application.endpointGroups;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.infrastructure.security;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.desktop.command;

/// <summary>
/// Ingests a desktop tracking window: persists the raw entries, and — for entries a pattern mapping
/// resolves to an activity — attributes the active seconds to that activity's time ledger.
/// </summary>
/// <remarks>
/// ⚠ <b>Ingest only. This endpoint must not write into another slice.</b> It used to do exactly that:
/// it wrote <c>ActivityHistory</c> rows inline (History), transitioned <c>PlannerTask.Status</c>
/// (Planning) and ticked off to-do and routine items (TodoLists, Routines) — three write-direction
/// dependencies, which is what kept Tracking from being extracted at all. Both are now seams owned by
/// Core:
/// <list type="bullet">
/// <item><see cref="IActivityTimeAttributionSink"/> takes the attribution write (History implements it).</item>
/// <item><see cref="ActivityTimeRecordedEvent"/> carries the day totals to whoever automates completion.</item>
/// </list>
/// Restoring either direct write re-couples Tracking to three slices; it will compile only for as long
/// as this file lives in the host.
/// </remarks>
[AllowExtensionClients]
public class DesktopActivityHeartbeatEndpoint(DbContext dbContext, IActivityTimeAttributionSink attributionSink)
    : Endpoint<DesktopActivityWindowDto>
{
    public override void Configure()
    {
        Post("/heartbeat");
        Summary(s =>
        {
            s.Summary = "Process desktop activity heartbeat";
            s.Description = "Records desktop activity data from client, updates activity history, and automates planner task/todo completion based on tracked time";
            s.Response<int>(200, "Success - returns count of processed entries");
            s.Response(400, "Bad request");
        });
        Validator<DesktopActivityHeartbeatValidator>();
        Group<ActivityTrackingDesktopGroup>();
        Policies(PortalAuthorizationPolicies.ActivityTracking);
    }

    public override async Task HandleAsync(DesktopActivityWindowDto req, CancellationToken ct)
    {
        var userId = User.GetId();
        var processedCount = 0;
        var attributedActivityIds = new HashSet<long>();

        var mappings = await dbContext.Set<TrackerDesktopMappingByPattern>()
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
                WindowStart = req.WindowStart,
                ProcessName = entry.ProcessName,
                ProductName = entry.ProductName,
                WindowTitle = entry.WindowTitle,
                ExecutablePath = entry.ExecutablePath,
                IsFullscreen = entry.IsFullscreen,
                ActiveSeconds = entry.ActiveSeconds,
                BackgroundSeconds = entry.BackgroundSeconds,
                IsPlayingSound = entry.IsPlayingSound,
                ActiveMonitor = entry.ActiveMonitor
            };

            dbContext.Set<DesktopActivityEntry>().Add(record);
            processedCount++;

            if (match?.ActivityId is { } activityId)
            {
                attributedActivityIds.Add(activityId);
                await attributionSink.AttributeAsync(dbContext, userId, activityId, req.WindowStart, entry.ActiveSeconds, ct);
            }
        }

        // One save for the raw entries and their attribution together, which is the sink's contract —
        // an implementation that saved on its own could leave entries recorded but unattributed.
        await dbContext.SaveChangesAsync(ct);

        await PublishAttributionAsync(userId, req.WindowStart, attributedActivityIds, ct);

        await Send.ResponseAsync(processedCount, cancellation: ct);
    }

    /// <summary>
    /// Announces the day totals for every activity this batch touched. Read <em>after</em> the save, so
    /// the batch's own seconds are included, and expressed as whole-day totals rather than the batch
    /// delta because every completion threshold downstream is cumulative.
    /// </summary>
    private async Task PublishAttributionAsync(long userId, DateTime windowStart, HashSet<long> attributedActivityIds, CancellationToken ct)
    {
        if (attributedActivityIds.Count == 0)
            return;

        var day = DateOnly.FromDateTime(windowStart);

        var totals = new List<ActivityDayTotal>(attributedActivityIds.Count);
        foreach (var activityId in attributedActivityIds)
            totals.Add(new ActivityDayTotal(
                activityId,
                await attributionSink.TotalSecondsOnDayAsync(dbContext, userId, activityId, day, ct)));

        await new ActivityTimeRecordedEvent(userId, day, totals).PublishAsync(Mode.WaitForAll, ct);
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
        if (value == null)
            return false;
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