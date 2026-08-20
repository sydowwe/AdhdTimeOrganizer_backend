using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.desktop.dashboard;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.helper;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.desktop.query.dashboard;

public class DesktopTimelineEndpoint(DbContext dbContext, IUserTimeZoneResolver timeZones)
    : Endpoint<BaseTimelineRequest, DesktopTimelineResponse>
{
    public override void Configure()
    {
        Post("/activity-tracking/desktop/timeline");
        Summary(s =>
        {
            s.Summary = "Get desktop process usage timeline with context awareness";
            s.Description = "Returns primary sessions, detail sessions, and background sessions with context switch detection and merging";
            s.Response<DesktopTimelineResponse>(200, "Success");
            s.Response(400, "Bad request");
        });
        Validator<BaseTimelineValidator>();
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

        var rawData = await dbContext.Set<DesktopActivityEntry>()
            .Where(x => x.UserId == userId)
            .Where(x => x.WindowStart >= from && x.WindowStart < to)
            .OrderBy(x => x.WindowStart)
            .ThenBy(x => x.ProcessName)
            .ToListAsync(ct);

        // Keyed on the process name, labelled with the product name. The session algorithm itself is
        // shared with the web-extension timeline and the three focus-metrics dashboards -- see
        // TimelineSegmentBuilder for why it is not transcribed per source any more.
        var (primary, detail) = TimelineSegmentBuilder.Build(
            rawData.Select(r => new ActivityMinute(r.WindowStart, r.ProcessName, r.ProductName, r.ActiveSeconds)));

        // The background lane names each process by the first non-empty product name seen anywhere in
        // its rows, rather than by whichever row happened to open the run -- a process whose first
        // background minute carries a blank product name is still that product for the whole day.
        var backgroundLabels = rawData
            .GroupBy(r => r.ProcessName, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => r.ProductName).FirstOrDefault(p => !string.IsNullOrEmpty(p)) ?? g.Key,
                StringComparer.Ordinal);

        var background = TimelineSegmentBuilder.BuildBackground(rawData.Select(r =>
            new ActivityMinute(r.WindowStart, r.ProcessName, backgroundLabels[r.ProcessName], r.BackgroundSeconds)));

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

        var response = new DesktopTimelineResponse
        {
            PrimarySessions = primarySessions,
            DetailSessions = detailSessions,
            BackgroundSessions = backgroundSessions
        };

        await Send.ResponseAsync(response, cancellation: ct);
    }

    private static DesktopTimelineSession ToSession(TimelineSegment segment) => new()
    {
        Id = 0,
        ProcessName = segment.Key,
        ProductName = segment.Label,
        StartedAt = segment.StartedAt,
        EndedAt = segment.EndedAt,
        DurationSeconds = segment.DurationSeconds,
        TotalSeconds = segment.TotalSeconds
    };
}
