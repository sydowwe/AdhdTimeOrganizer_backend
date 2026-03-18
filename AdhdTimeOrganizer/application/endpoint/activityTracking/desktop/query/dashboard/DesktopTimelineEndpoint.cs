using AdhdTimeOrganizer.application.dto.request.activityTracking;
using AdhdTimeOrganizer.application.dto.response.activityTracking.desktop.dashboard;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.desktop.query.dashboard;

public class DesktopTimelineEndpoint(AppDbContext dbContext)
    : Endpoint<WebExtensionTimelineRequest, DesktopTimelineResponse>
{
    private const int ContextWindowRadius = 2;
    private const int ContextSwitchThresholdMinutes = 2;
    private const int MinDetailSeconds = 60;

    public override void Configure()
    {
        Post("/activity-tracking/desktop/timeline");
        Validator<WebExtensionTimelineValidator>();
    }

    public override async Task HandleAsync(WebExtensionTimelineRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        var (from, to) = req.ToDateTimeRange();

        var rawData = await dbContext.DesktopActivityEntries
            .Where(x => x.UserId == userId)
            .Where(x => x.WindowStart >= from && x.WindowStart < to)
            .OrderBy(x => x.WindowStart)
            .ThenBy(x => x.ProcessName)
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

        var response = new DesktopTimelineResponse
        {
            PrimarySessions = primarySessions,
            DetailSessions = detailSessions,
            BackgroundSessions = backgroundSessions
        };

        await SendAsync(response, cancellation: ct);
    }

    private static (List<DesktopTimelineSession> primary, List<DesktopTimelineSession> detail) BuildTimeline(
        List<DesktopActivityEntry> rawData, Func<DesktopActivityEntry, int> secondsSelector)
    {
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
                    contextScores.TryAdd(record.ProcessName, 0);
                    contextScores[record.ProcessName] += secondsSelector(record);
                }
            }

            var ordered = currentRecords
                .OrderByDescending(r => contextScores.GetValueOrDefault(r.ProcessName, 0))
                .ThenByDescending(r => secondsSelector(r))
                .ToList();

            var primary = ordered[0];
            primaryMinutes.Add(new MinuteEntry(
                currentMinute, primary.ProcessName, primary.ProductName, secondsSelector(primary)));

            foreach (var other in ordered.Skip(1))
            {
                secondaryMinutes.Add(new MinuteEntry(
                    other.WindowStart, other.ProcessName, other.ProductName, secondsSelector(other)));
            }
        }

        var primarySessions = BuildSessionsFromMinutes(primaryMinutes);

        var absorbedSwitches = AbsorbContextSwitches(primarySessions, ContextSwitchThresholdMinutes);

        var detailSessions = BuildSessionsFromMinutes(secondaryMinutes);
        detailSessions.AddRange(absorbedSwitches);
        detailSessions = MergeAdjacentSessions(detailSessions);
        detailSessions = detailSessions.Where(s => s.TotalSeconds >= MinDetailSeconds).ToList();

        return (primarySessions, detailSessions);
    }

    private static List<DesktopTimelineSession> BuildSessionsFromMinutes(List<MinuteEntry> minutes)
    {
        var sessions = new List<DesktopTimelineSession>();
        DesktopTimelineSession? current = null;

        foreach (var min in minutes.OrderBy(m => m.WindowStart).ThenBy(m => m.ProcessName))
        {
            var windowEnd = min.WindowStart.AddMinutes(1);

            if (current != null && current.ProcessName == min.ProcessName && min.WindowStart == current.EndedAt)
            {
                current.EndedAt = windowEnd;
                current.DurationSeconds = (int)(current.EndedAt - current.StartedAt).TotalSeconds;
                current.TotalSeconds += min.Seconds;
            }
            else
            {
                if (current != null)
                    sessions.Add(current);

                current = new DesktopTimelineSession
                {
                    Id = 0,
                    ProcessName = min.ProcessName,
                    ProductName = min.ProductName,
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

    private static List<DesktopTimelineSession> AbsorbContextSwitches(
        List<DesktopTimelineSession> sessions, int thresholdMinutes)
    {
        var absorbed = new List<DesktopTimelineSession>();
        bool merged;

        do
        {
            merged = false;
            for (var i = 0; i < sessions.Count; i++)
            {
                for (var j = i + 1; j < Math.Min(i + 4, sessions.Count); j++)
                {
                    if (sessions[j].ProcessName != sessions[i].ProcessName) continue;

                    var gapDuration = 0;
                    for (var k = i + 1; k < j; k++)
                        gapDuration += sessions[k].DurationSeconds;

                    if (gapDuration > thresholdMinutes * 60) continue;

                    for (var k = i + 1; k < j; k++)
                        absorbed.Add(sessions[k]);

                    sessions[i] = sessions[i] with
                    {
                        EndedAt = sessions[j].EndedAt,
                        DurationSeconds = (int)(sessions[j].EndedAt - sessions[i].StartedAt).TotalSeconds,
                        TotalSeconds = sessions[i].TotalSeconds + sessions[j].TotalSeconds,
                    };

                    sessions.RemoveRange(i + 1, j - i);
                    merged = true;
                    break;
                }

                if (merged) break;
            }
        } while (merged);

        return absorbed;
    }

    private static List<DesktopTimelineSession> MergeAdjacentSessions(List<DesktopTimelineSession> sessions)
    {
        if (sessions.Count == 0) return sessions;

        sessions = sessions.OrderBy(s => s.StartedAt).ThenBy(s => s.ProcessName).ToList();
        var result = new List<DesktopTimelineSession> { sessions[0] };

        for (var i = 1; i < sessions.Count; i++)
        {
            var last = result[^1];
            var current = sessions[i];

            if (last.ProcessName == current.ProcessName && current.StartedAt <= last.EndedAt)
            {
                var newEnd = current.EndedAt > last.EndedAt ? current.EndedAt : last.EndedAt;
                result[^1] = last with
                {
                    EndedAt = newEnd,
                    DurationSeconds = (int)(newEnd - last.StartedAt).TotalSeconds,
                    TotalSeconds = last.TotalSeconds + current.TotalSeconds,
                };
            }
            else
            {
                result.Add(current);
            }
        }

        return result;
    }

    private static List<DesktopTimelineSession> BuildBackgroundTimeline(List<DesktopActivityEntry> rawData)
    {
        var sessions = new List<DesktopTimelineSession>();

        var byProcess = rawData
            .Where(r => r.BackgroundSeconds > 0)
            .GroupBy(r => r.ProcessName);

        foreach (var processGroup in byProcess)
        {
            var processSessions = new List<DesktopTimelineSession>();
            DesktopTimelineSession? current = null;

            var productName = processGroup
                .Where(x => !string.IsNullOrEmpty(x.ProductName))
                .Select(x => x.ProductName)
                .FirstOrDefault() ?? processGroup.Key;

            foreach (var record in processGroup.OrderBy(r => r.WindowStart))
            {
                var windowEnd = record.WindowStart.AddMinutes(1);

                if (current != null && record.WindowStart == current.EndedAt)
                {
                    current.EndedAt = windowEnd;
                    current.DurationSeconds = (int)(current.EndedAt - current.StartedAt).TotalSeconds;
                    current.TotalSeconds += record.BackgroundSeconds;
                }
                else
                {
                    if (current != null)
                        processSessions.Add(current);

                    current = new DesktopTimelineSession
                    {
                        Id = 0,
                        ProcessName = processGroup.Key,
                        ProductName = productName,
                        StartedAt = record.WindowStart,
                        EndedAt = windowEnd,
                        DurationSeconds = 60,
                        TotalSeconds = record.BackgroundSeconds,
                    };
                }
            }

            if (current != null)
                processSessions.Add(current);

            sessions.AddRange(BridgeGaps(processSessions));
        }

        return sessions
            .Where(s => s.TotalSeconds >= MinDetailSeconds)
            .OrderBy(s => s.StartedAt)
            .ThenBy(s => s.ProcessName)
            .ToList();
    }

    private static List<DesktopTimelineSession> BridgeGaps(List<DesktopTimelineSession> sessions)
    {
        if (sessions.Count <= 1) return sessions;

        var result = new List<DesktopTimelineSession> { sessions[0] };

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
                };
            }
            else
            {
                result.Add(current);
            }
        }

        return result;
    }

    private record MinuteEntry(DateTime WindowStart, string ProcessName, string? ProductName, int Seconds);
}
