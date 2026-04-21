using AdhdTimeOrganizer.application.dto.request.activityTracking.android;
using AdhdTimeOrganizer.application.dto.response.activityTracking.android.dashboard;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.android.query;

public class AndroidTimelineEndpoint(AppDbContext db) : Endpoint<AndroidTimelineRequest, AndroidTimelineResponse>
{
    public override void Configure()
    {
        Post("/activity-tracking/android/timeline");
        Validator<AndroidTimelineValidator>();
    }

    public override async Task HandleAsync(AndroidTimelineRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var (from, to) = req.ToDateTimeRange();

        var rawSessions = await db.AndroidSessionDataEntries
            .Where(x => x.UserId == userId)
            .Where(x => x.SessionStartUtc >= from && x.SessionStartUtc < to)
            .OrderBy(x => x.SessionStartUtc)
            .ToListAsync(ct);

        var totalSecondsByLabel = rawSessions
            .GroupBy(x => x.AppLabel)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.DurationSeconds));

        var sessions = rawSessions
            .Select(s => new AndroidTimelineSession
            {
                PackageName = s.PackageName,
                AppLabel = s.AppLabel,
                StartedAt = s.SessionStartUtc,
                EndedAt = s.SessionEndUtc,
                DurationSeconds = s.DurationSeconds,
                TotalSeconds = totalSecondsByLabel[s.AppLabel]
            })
            .ToList();

        if (req.MinSeconds is > 0)
        {
            sessions = sessions.Where(s => s.DurationSeconds >= req.MinSeconds.Value).ToList();
        }

        long id = 1;
        foreach (var session in sessions)
            session.Id = id++;

        await Send.ResponseAsync(new AndroidTimelineResponse { Sessions = sessions }, cancellation: ct);
    }
}
