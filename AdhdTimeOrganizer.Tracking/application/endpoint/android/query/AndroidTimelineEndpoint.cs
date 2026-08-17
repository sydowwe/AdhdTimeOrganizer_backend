using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.android.dashboard;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.android.query;

public class AndroidTimelineEndpoint(DbContext db, IUserTimeZoneResolver timeZones) : Endpoint<AndroidTimelineRequest, AndroidTimelineResponse>
{
    public override void Configure()
    {
        Post("/activity-tracking/android/timeline");
        Summary(s =>
        {
            s.Summary = "Get Android app usage timeline";
            s.Description = "Returns chronological list of app sessions for a given date range with optional minimum duration filter";
            s.Response<AndroidTimelineResponse>(200, "Success");
            s.Response(400, "Bad request");
        });
        Validator<AndroidTimelineValidator>();
    }

    public override async Task HandleAsync(AndroidTimelineRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var (from, to) = req.ToDateTimeRange(await timeZones.GetAsync(userId, ct));

        var rawSessions = await db.Set<AndroidSessionData>()
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
            sessions = sessions.Where(s => s.DurationSeconds >= req.MinSeconds.Value).ToList();

        long id = 1;
        foreach (var session in sessions)
            session.Id = id++;

        await Send.ResponseAsync(new AndroidTimelineResponse { Sessions = sessions }, cancellation: ct);
    }
}