using AdhdTimeOrganizer.application.dto.request.activityTracking;
using AdhdTimeOrganizer.application.dto.response.activityTracking;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.webExtension.query;

public class WebExtensionTimelineEndpoint(AppDbContext dbContext)
    : Endpoint<WebExtensionTimelineRequest, WebExtensionTimelineResponse>
{
    public override void Configure()
    {
        Get("/activity-tracking/web-extension/timeline");
        Policies("ActivityTracking");
        Validator<WebExtensionTimelineValidator>();
    }

    public override async Task HandleAsync(WebExtensionTimelineRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        // 1. Fetch raw 5-min window data ordered by time
        var rawData = await dbContext.WebExtensionData
            .Where(x => x.UserId == userId)
            .Where(x => x.WindowStart >= req.From && x.WindowStart < req.To)
            .OrderBy(x => x.WindowStart)
            .ThenBy(x => x.Domain)
            .ToListAsync(ct);

        // 2. Build separate sessions for active and background
        var activeSessions = BuildSessions(rawData, isBackground: false);
        var backgroundSessions = BuildSessions(rawData, isBackground: true);

        // 3. Apply minimum duration filter
        if (req.MinSeconds.HasValue && req.MinSeconds > 0)
        {
            activeSessions = activeSessions
                .Where(s => s.TotalSeconds >= req.MinSeconds.Value)
                .ToList();
            backgroundSessions = backgroundSessions
                .Where(s => s.TotalSeconds >= req.MinSeconds.Value)
                .ToList();
        }

        // 4. Assign IDs for frontend keys
        long id = 1;
        foreach (var session in activeSessions.Concat(backgroundSessions))
        {
            session.Id = id++;
        }

        var response = new WebExtensionTimelineResponse
        {
            From = req.From,
            To = req.To,
            ActiveSessions = activeSessions,
            BackgroundSessions = backgroundSessions
        };

        await SendAsync(response, cancellation: ct);
    }

    private List<TimelineSession> BuildSessions(List<WebExtensionData> rawData, bool isBackground)
    {
        var sessions = new List<TimelineSession>();
        TimelineSession? currentSession = null;
        DateTime? lastWindowEnd = null;

        foreach (var record in rawData)
        {
            var seconds = isBackground ? record.BackgroundSeconds : record.ActiveSeconds;

            // Skip if no activity of this type in this window
            if (seconds <= 0)
            {
                // If we had an ongoing session, close it
                if (currentSession != null)
                {
                    sessions.Add(currentSession);
                    currentSession = null;
                    lastWindowEnd = null;
                }
                continue;
            }

            var windowEnd = record.WindowStart.AddMinutes(5);

            // Check if this continues the current session
            // Same domain AND adjacent window (no gap)
            var isAdjacent = lastWindowEnd.HasValue && record.WindowStart == lastWindowEnd.Value;
            var sameDomain = currentSession?.Domain == record.Domain;

            if (currentSession != null && sameDomain && isAdjacent)
            {
                // Extend current session
                currentSession.EndedAt = windowEnd;
                currentSession.DurationSeconds = (int)(currentSession.EndedAt - currentSession.StartedAt).TotalSeconds;
                currentSession.TotalSeconds += seconds;

                // Update URL if this window has more time on a different URL
                // (simplified: just keep track of most common URL)
                if (!string.IsNullOrEmpty(record.Url))
                {
                    currentSession.Url ??= record.Url;
                }
            }
            else
            {
                // Start new session (close previous if exists)
                if (currentSession != null)
                {
                    sessions.Add(currentSession);
                }

                currentSession = new TimelineSession
                {
                    Domain = record.Domain,
                    Url = record.Url,
                    StartedAt = record.WindowStart,
                    EndedAt = windowEnd,
                    DurationSeconds = 5 * 60,  // 5 minutes initially
                    TotalSeconds = seconds
                };
            }

            lastWindowEnd = windowEnd;
        }

        // Don't forget the last session
        if (currentSession != null)
        {
            sessions.Add(currentSession);
        }

        return sessions.OrderBy(s => s.StartedAt).ToList();
    }
}
