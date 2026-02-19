using AdhdTimeOrganizer.application.dto.request.activityTracking;
using AdhdTimeOrganizer.application.dto.response.activityTracking.timeline;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.webExtension.query;

public class WebExtensionTimelineEndpoint(AppDbContext dbContext)
    : Endpoint<WebExtensionTimelineRequest, WebExtensionTimelineResponse>
{
    private const int ContextWindowRadius = 2;
    private const int ContextSwitchThresholdMinutes = 2;
    private const int MinDetailSeconds = 60;

    public override void Configure()
    {
        Post("/activity-tracking/web-extension/timeline");
        Validator<WebExtensionTimelineValidator>();
    }

    public override async Task HandleAsync(WebExtensionTimelineRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        var (from, to) = req.ToDateTimeRange();

        var rawData = await dbContext.WebExtensionData
            .Where(x => x.UserId == userId)
            .Where(x => x.WindowStart >= from && x.WindowStart < to)
            .OrderBy(x => x.WindowStart)
            .ThenBy(x => x.Domain)
            .ToListAsync(ct);

        var (primarySessions, detailSessions) = BuildTimeline(rawData, r => r.ActiveSeconds);
        var backgroundSessions = BuildBackgroundTimeline(rawData);

        if (req.MinSeconds.HasValue && req.MinSeconds > 0)
        {
            primarySessions = primarySessions
                .Where(s => s.TotalSeconds >= req.MinSeconds.Value)
                .ToList();
            backgroundSessions = backgroundSessions
                .Where(s => s.TotalSeconds >= req.MinSeconds.Value)
                .ToList();
        }

        long id = 1;
        foreach (var session in primarySessions.Concat(detailSessions).Concat(backgroundSessions))
            session.Id = id++;

        var response = new WebExtensionTimelineResponse
        {
            PrimarySessions = primarySessions,
            DetailSessions = detailSessions,
            BackgroundSessions = backgroundSessions
        };

        await SendAsync(response, cancellation: ct);
    }

    /// <summary>
    /// Builds a two-row timeline using sliding window context scoring:
    /// - Primary: dominant domain per minute (decided by ±ContextWindowRadius context), with short switches absorbed
    /// - Detail: secondary concurrent activity + absorbed context switches
    /// Works for both active and background by passing the appropriate seconds selector.
    /// </summary>
    private static (List<TimelineSession> primary, List<TimelineSession> detail) BuildTimeline(
        List<WebExtensionData> rawData, Func<WebExtensionData, int> secondsSelector)
    {
        // Step 1: Index records by minute, filtering to those with seconds > 0
        var minuteMap = rawData
            .GroupBy(r => r.WindowStart)
            .ToDictionary(
                g => g.Key,
                g => g.Where(r => secondsSelector(r) > 0).ToList());

        var sortedMinutes = minuteMap
            .Where(kv => kv.Value.Count > 0)
            .Select(kv => kv.Key)
            .OrderBy(k => k)
            .ToList();

        var primaryMinutes = new List<MinuteEntry>();
        var secondaryMinutes = new List<MinuteEntry>();

        // Step 2: For each minute, use ±ContextWindowRadius sliding window to score domains.
        // The domain with the highest cumulative seconds across the window wins primary,
        // so a 1-min context switch inside a longer session doesn't break it.
        for (var i = 0; i < sortedMinutes.Count; i++)
        {
            var currentMinute = sortedMinutes[i];
            var currentRecords = minuteMap[currentMinute];

            var contextScores = new Dictionary<string, int>();

            for (var offset = -ContextWindowRadius; offset <= ContextWindowRadius; offset++)
            {
                var idx = i + offset;
                if (idx < 0 || idx >= sortedMinutes.Count) continue;

                var neighborMinute = sortedMinutes[idx];

                if (Math.Abs((neighborMinute - currentMinute).TotalMinutes) > ContextWindowRadius)
                    continue;

                foreach (var record in minuteMap[neighborMinute])
                {
                    contextScores.TryAdd(record.Domain, 0);
                    contextScores[record.Domain] += secondsSelector(record);
                }
            }

            var ordered = currentRecords
                .OrderByDescending(r => contextScores.GetValueOrDefault(r.Domain, 0))
                .ThenByDescending(r => secondsSelector(r))
                .ToList();

            var primary = ordered[0];
            primaryMinutes.Add(new MinuteEntry(
                currentMinute, primary.Domain, primary.Url, secondsSelector(primary)));

            foreach (var other in ordered.Skip(1))
            {
                secondaryMinutes.Add(new MinuteEntry(
                    other.WindowStart, other.Domain, other.Url, secondsSelector(other)));
            }
        }

        // Step 3: Build primary sessions chronologically (no overlaps since one domain per minute)
        var primarySessions = BuildSessionsFromMinutes(primaryMinutes);

        // Step 4: Absorb remaining short context switches between same-domain primary sessions
        var absorbedSwitches = AbsorbContextSwitches(primarySessions, ContextSwitchThresholdMinutes);

        // Step 5: Build detail sessions from secondary activity + absorbed switches
        var detailSessions = BuildSessionsFromMinutes(secondaryMinutes);
        detailSessions.AddRange(absorbedSwitches);
        detailSessions = MergeAdjacentSessions(detailSessions);
        detailSessions = detailSessions.Where(s => s.TotalSeconds >= MinDetailSeconds).ToList();

        return (primarySessions, detailSessions);
    }

    /// <summary>
    /// Merges chronologically adjacent same-domain minutes into sessions.
    /// </summary>
    private static List<TimelineSession> BuildSessionsFromMinutes(List<MinuteEntry> minutes)
    {
        var sessions = new List<TimelineSession>();
        TimelineSession? current = null;

        foreach (var min in minutes.OrderBy(m => m.WindowStart).ThenBy(m => m.Domain))
        {
            var windowEnd = min.WindowStart.AddMinutes(1);

            if (current != null && current.Domain == min.Domain && min.WindowStart == current.EndedAt)
            {
                current.EndedAt = windowEnd;
                current.DurationSeconds = (int)(current.EndedAt - current.StartedAt).TotalSeconds;
                current.TotalSeconds += min.Seconds;
                current.Url ??= min.Url;
            }
            else
            {
                if (current != null)
                    sessions.Add(current);

                current = new TimelineSession
                {
                    Id = 0,
                    Domain = min.Domain,
                    Url = min.Url,
                    StartedAt = min.WindowStart,
                    EndedAt = windowEnd,
                    DurationSeconds = 60,
                    TotalSeconds = min.Seconds,
                };
            }
        }

        if (current != null)
            sessions.Add(current);

        return sessions;
    }

    /// <summary>
    /// Scans primary sessions and merges same-domain sessions separated by short interruptions.
    /// The short interrupting sessions are returned as "absorbed" context switches for the detail row.
    /// Looks up to 3 sessions ahead to handle multi-session gaps (A → B → C → A).
    /// </summary>
    private static List<TimelineSession> AbsorbContextSwitches(
        List<TimelineSession> sessions, int thresholdMinutes)
    {
        var absorbed = new List<TimelineSession>();
        bool merged;

        do
        {
            merged = false;
            for (var i = 0; i < sessions.Count; i++)
            {
                for (var j = i + 1; j < Math.Min(i + 4, sessions.Count); j++)
                {
                    if (sessions[j].Domain != sessions[i].Domain) continue;

                    var gapDuration = 0;
                    for (var k = i + 1; k < j; k++)
                        gapDuration += sessions[k].DurationSeconds;

                    if (gapDuration > thresholdMinutes * 60) continue;

                    // Move gap sessions to detail
                    for (var k = i + 1; k < j; k++)
                        absorbed.Add(sessions[k]);

                    // Merge sessions[j] into sessions[i]
                    sessions[i] = sessions[i] with
                    {
                        EndedAt = sessions[j].EndedAt,
                        DurationSeconds = (int)(sessions[j].EndedAt - sessions[i].StartedAt).TotalSeconds,
                        TotalSeconds = sessions[i].TotalSeconds + sessions[j].TotalSeconds,
                        Url = sessions[i].Url ?? sessions[j].Url,
                    };

                    // Remove gap sessions and sessions[j]
                    sessions.RemoveRange(i + 1, j - i);
                    merged = true;
                    break;
                }

                if (merged) break;
            }
        } while (merged);

        return absorbed;
    }

    /// <summary>
    /// Merges overlapping or adjacent same-domain sessions into single sessions.
    /// </summary>
    private static List<TimelineSession> MergeAdjacentSessions(List<TimelineSession> sessions)
    {
        if (sessions.Count == 0) return sessions;

        sessions = sessions.OrderBy(s => s.StartedAt).ThenBy(s => s.Domain).ToList();
        var result = new List<TimelineSession> { sessions[0] };

        for (var i = 1; i < sessions.Count; i++)
        {
            var last = result[^1];
            var current = sessions[i];

            if (last.Domain == current.Domain && current.StartedAt <= last.EndedAt)
            {
                var newEnd = current.EndedAt > last.EndedAt ? current.EndedAt : last.EndedAt;
                result[^1] = last with
                {
                    EndedAt = newEnd,
                    DurationSeconds = (int)(newEnd - last.StartedAt).TotalSeconds,
                    TotalSeconds = last.TotalSeconds + current.TotalSeconds,
                    Url = last.Url ?? current.Url,
                };
            }
            else
            {
                result.Add(current);
            }
        }

        return result;
    }

    /// <summary>
    /// Builds background sessions grouped per domain (allowing overlap between domains),
    /// with short gaps within a single domain's activity bridged into continuous sessions.
    /// </summary>
    private static List<TimelineSession> BuildBackgroundTimeline(List<WebExtensionData> rawData)
    {
        var sessions = new List<TimelineSession>();

        var byDomain = rawData
            .Where(r => r.BackgroundSeconds > 0)
            .GroupBy(r => r.Domain);

        foreach (var domainGroup in byDomain)
        {
            var domainSessions = new List<TimelineSession>();
            TimelineSession? current = null;

            foreach (var record in domainGroup.OrderBy(r => r.WindowStart))
            {
                var windowEnd = record.WindowStart.AddMinutes(1);

                if (current != null && record.WindowStart == current.EndedAt)
                {
                    current.EndedAt = windowEnd;
                    current.DurationSeconds = (int)(current.EndedAt - current.StartedAt).TotalSeconds;
                    current.TotalSeconds += record.BackgroundSeconds;
                    current.Url ??= record.Url;
                }
                else
                {
                    if (current != null)
                        domainSessions.Add(current);

                    current = new TimelineSession
                    {
                        Id = 0,
                        Domain = record.Domain,
                        Url = record.Url,
                        StartedAt = record.WindowStart,
                        EndedAt = windowEnd,
                        DurationSeconds = 60,
                        TotalSeconds = record.BackgroundSeconds,
                    };
                }
            }

            if (current != null)
                domainSessions.Add(current);

            // Bridge short gaps within this domain's sessions
            sessions.AddRange(BridgeGaps(domainSessions));
        }

        return sessions
            .Where(s => s.TotalSeconds >= MinDetailSeconds)
            .OrderBy(s => s.StartedAt)
            .ThenBy(s => s.Domain)
            .ToList();
    }

    /// <summary>
    /// Merges sessions of the same domain separated by gaps ≤ threshold into continuous sessions.
    /// </summary>
    private static List<TimelineSession> BridgeGaps(List<TimelineSession> sessions)
    {
        if (sessions.Count <= 1) return sessions;

        var result = new List<TimelineSession> { sessions[0] };

        for (var i = 1; i < sessions.Count; i++)
        {
            var last = result[^1];
            var current = sessions[i];
            var gapMinutes = (current.StartedAt - last.EndedAt).TotalMinutes;

            if (gapMinutes <= ContextSwitchThresholdMinutes)
            {
                result[^1] = last with
                {
                    EndedAt = current.EndedAt,
                    DurationSeconds = (int)(current.EndedAt - last.StartedAt).TotalSeconds,
                    TotalSeconds = last.TotalSeconds + current.TotalSeconds,
                    Url = last.Url ?? current.Url,
                };
            }
            else
            {
                result.Add(current);
            }
        }

        return result;
    }

    private record MinuteEntry(DateTime WindowStart, string Domain, string? Url, int Seconds);
}
