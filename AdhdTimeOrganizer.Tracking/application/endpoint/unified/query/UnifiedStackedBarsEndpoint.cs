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
/// The merged day as bands. The bands are <c>DailyWindowSet.Tile</c>'s, unchanged from the per-source
/// dashboards — the same client component draws both, and it generates its own empty slots on that
/// alignment, so bands with no activity are simply omitted here.
/// </summary>
public class UnifiedStackedBarsEndpoint(DbContext db, IUserTimeZoneResolver timeZones)
    : Endpoint<UnifiedStackedBarsRequest, List<UnifiedStackedBarsWindowDto>>
{
    public override void Configure()
    {
        Post("/activity-tracking/unified/stacked-bars");
        Validator<UnifiedStackedBarsValidator>();
        Summary(s =>
        {
            s.Summary = "Get the merged activity aggregated into bands for a stacked bar chart";
            s.Description =
                "Aggregates the selected trackers, de-overlapped, into the same bands the per-source " +
                "stacked bars use: under a day they tile each day's time-of-day window, at a day and " +
                "over they tile the span in whole days.";
            s.Response<List<UnifiedStackedBarsWindowDto>>(200, "Success");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(UnifiedStackedBarsRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        var windows = req.ToDailyWindows(await timeZones.GetAsync(userId, ct));

        var span = await UnifiedSpan.BuildAsync(db, userId, windows, req.SelectedSources(), ct);
        var buckets = windows.Tile(req.WindowMinutes);

        var response = span.Merge.Minutes
            .GroupBy(minute => DailyWindowSet.BucketIndexOf(buckets, minute.Minute))
            .Where(band => band.Key >= 0)
            .Select(band => BuildWindow(buckets[band.Key], band.ToList()))
            .Where(band => band.Items.Count > 0)
            .OrderBy(band => band.WindowStart)
            .ToList();

        await Send.ResponseAsync(response, cancellation: ct);
    }

    /// <summary>
    /// One band. <b>Items are merged by label across sources</b>: a per-(label, source) split would
    /// draw the same application twice in one column in the same colour, and a stacked bar has no room
    /// to explain why. The source dimension is carried by the timeline's lanes and the filter's own
    /// totals, which is where it can be read.
    /// </summary>
    private static UnifiedStackedBarsWindowDto BuildWindow(AggregationWindow bucket, List<MergedMinute> minutes)
    {
        var groups = minutes
            .GroupBy(minute => minute.Label, StringComparer.Ordinal)
            .Select(g => new
            {
                Label = g.Key,
                Active = g.Sum(m => m.ActiveSeconds),
                Background = g.Sum(m => m.BackgroundSeconds),
                Sources = g.Select(m => m.Source).Distinct().OrderBy(source => source).ToList()
            })
            .OrderBy(g => g.Label, StringComparer.Ordinal)
            .ToList();

        // Rounded band by band out of one pool, so a band's segments sum to the band's own height.
        var values = new List<double>(groups.Count * 2);

        foreach (var group in groups)
        {
            values.Add(group.Active);
            values.Add(group.Background);
        }

        var allocated = SecondsAllocator.Allocate(values);

        var items = new List<UnifiedStackedBarsItemDto>(groups.Count);

        for (var i = 0; i < groups.Count; i++)
        {
            if (allocated[i * 2] + allocated[i * 2 + 1] <= 0)
                continue;

            items.Add(new UnifiedStackedBarsItemDto
            {
                Label = groups[i].Label,
                ActiveSeconds = allocated[i * 2],
                BackgroundSeconds = allocated[i * 2 + 1],
                Sources = groups[i].Sources.Select(source => source.ToWireName()).ToList()
            });
        }

        return new UnifiedStackedBarsWindowDto
        {
            WindowStart = bucket.Start,
            WindowEnd = bucket.End,
            Items = items
                .OrderByDescending(item => item.ActiveSeconds + item.BackgroundSeconds)
                .ToList()
        };
    }
}
