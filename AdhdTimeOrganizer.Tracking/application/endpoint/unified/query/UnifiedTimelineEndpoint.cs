using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.unified;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.unified;
using AdhdTimeOrganizer.Tracking.application.service.unified;
using AdhdTimeOrganizer.Tracking.application.validator.unified;
using AdhdTimeOrganizer.Tracking.domain.helper;
using AdhdTimeOrganizer.Tracking.domain.helper.unified;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.unified.query;

/// <summary>
/// One day, read top to bottom as a single day rather than as three transparencies laid over each
/// other.
///
/// <para><b>The lanes are the trackers</b>, not the active/detail/background split the per-source
/// timelines use, and they come back in precedence order so the overlap resolution reads down the
/// chart. Because no minute appears in more than one lane, a session in one lane never overlaps a
/// session in another — the invariant the whole picture rests on.</para>
///
/// <para><b>Single day only</b>, exactly as the three per-source timelines are: a merged month of
/// sessions is even less legible than one source's, and the client falls back to the stacked bars over
/// a range through the same code path.</para>
/// </summary>
public class UnifiedTimelineEndpoint(DbContext db, IUserTimeZoneResolver timeZones)
    : Endpoint<UnifiedTimelineRequest, UnifiedTimelineResponse>
{
    public override void Configure()
    {
        Post("/activity-tracking/unified/timeline");
        Validator<UnifiedTimelineValidator>();
        Summary(s =>
        {
            s.Summary = "Get one day as a merged timeline, one lane per tracker";
            s.Description =
                "Sessions built from the de-overlapped day, split into one lane per selected tracker. " +
                "No two sessions in different lanes overlap in wall-clock time.";
            s.Response<UnifiedTimelineResponse>(200, "Success");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(UnifiedTimelineRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        // The validator pins this dashboard to a single day, so the span is exactly one window.
        // Resolved through the same path as the other five anyway, so the over-midnight rule cannot
        // drift between them.
        var windows = req.ToDailyWindows(await timeZones.GetAsync(userId, ct));

        var span = await UnifiedSpan.BuildAsync(db, userId, windows, req.SelectedSources(), ct);
        var exclusive = span.ExclusiveMinutes();

        var response = new UnifiedTimelineResponse
        {
            WebExtensionSessions = LaneOf(exclusive, TrackingSource.WebExtension),
            DesktopSessions = LaneOf(exclusive, TrackingSource.Desktop),
            AndroidSessions = LaneOf(exclusive, TrackingSource.Android)
        };

        // Ids are unique across the whole response, not merely within a lane. Nothing in the client
        // breaks on a collision today -- the three lanes are separate loops -- but it is free insurance
        // against the day someone merges them, where a duplicate id silently drops a session.
        var id = 1;

        foreach (var session in response.WebExtensionSessions
                     .Concat(response.DesktopSessions)
                     .Concat(response.AndroidSessions))
            session.Id = id++;

        await Send.ResponseAsync(response, cancellation: ct);
    }

    /// <summary>
    /// One lane, built with the same session algorithm the per-source timelines use, so a lane reads
    /// the way that source's own timeline does — the ±2-minute vote for the dominant item and the short
    /// interruptions absorbed. Only the primary lane is kept: a detail lane inside a lane inside a
    /// merged chart is not a thing anyone can read.
    /// </summary>
    private static List<UnifiedTimelineSessionDto> LaneOf(
        List<(DateTime Minute, TrackingSource Source, string Label, string? Detail, double Seconds)> exclusive,
        TrackingSource source)
    {
        var minutes = exclusive
            .Where(m => m.Source == source)
            .Select(m => new ActivityMinute(
                m.Minute, m.Label, m.Detail, (int)Math.Round(m.Seconds, MidpointRounding.AwayFromZero)))
            .ToList();

        var (primary, _) = TimelineSegmentBuilder.Build(minutes);

        return primary
            .OrderBy(segment => segment.StartedAt)
            .Select(segment => new UnifiedTimelineSessionDto
            {
                Id = 0,
                Label = segment.Key,
                Url = segment.Label,
                StartedAt = segment.StartedAt,
                EndedAt = segment.EndedAt,
                DurationSeconds = segment.DurationSeconds,
                TotalSeconds = segment.TotalSeconds
            })
            .ToList();
    }
}
