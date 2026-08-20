using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.timeline;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.helper;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.webExtension.query;

public class WebExtensionTimelineEndpoint(DbContext dbContext, IUserTimeZoneResolver timeZones)
    : Endpoint<BaseTimelineRequest, WebExtensionTimelineResponse>
{
    public override void Configure()
    {
        Post("/activity-tracking/web-extension/timeline");
        Validator<BaseTimelineValidator>();
        Summary(s =>
        {
            s.Summary = "Get browsing activity as primary, detail, and background timeline sessions";
            s.Description = "Constructs a multi-row timeline of web activities with dominant activity track, context switches, and background activity with intelligent session merging";
            s.Response<WebExtensionTimelineResponse>(200, "Success");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(BaseTimelineRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        // The validator pins this dashboard to a single day, so the span is exactly one window and the
        // envelope is that window. Resolved through the same path as the other three anyway, so the
        // over-midnight rule cannot drift between them.
        var windows = req.ToDailyWindows(await timeZones.GetAsync(userId, ct));
        var from = windows.EnvelopeFrom;
        var to = windows.EnvelopeTo;

        var rawData = await dbContext.Set<WebExtensionActivityEntry>()
            .Where(x => x.UserId == userId)
            .Where(x => x.WindowStart >= from && x.WindowStart < to)
            .OrderBy(x => x.WindowStart)
            .ThenBy(x => x.Domain)
            .ToListAsync(ct);

        // Keyed on the domain, labelled with the most-seen URL. The session algorithm itself is shared
        // with the desktop timeline and the three focus-metrics dashboards -- see TimelineSegmentBuilder
        // for why it is not transcribed per source any more.
        var (primary, detail) = TimelineSegmentBuilder.Build(
            rawData.Select(r => new ActivityMinute(r.WindowStart, r.Domain, r.Url, r.ActiveSeconds)));

        var background = TimelineSegmentBuilder.BuildBackground(
            rawData.Select(r => new ActivityMinute(r.WindowStart, r.Domain, r.Url, r.BackgroundSeconds)));

        var primarySessions = primary.Select(ToSession).ToList();
        var detailSessions = detail.Select(ToSession).ToList();
        var backgroundSessions = background.Select(ToSession).ToList();

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

        await Send.ResponseAsync(response, cancellation: ct);
    }

    private static TimelineSession ToSession(TimelineSegment segment) => new()
    {
        Id = 0,
        Domain = segment.Key,
        Url = segment.Label,
        StartedAt = segment.StartedAt,
        EndedAt = segment.EndedAt,
        DurationSeconds = segment.DurationSeconds,
        TotalSeconds = segment.TotalSeconds
    };
}
