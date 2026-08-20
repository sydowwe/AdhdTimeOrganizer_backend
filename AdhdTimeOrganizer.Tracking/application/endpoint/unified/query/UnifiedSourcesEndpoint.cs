using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.unified;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.unified;
using AdhdTimeOrganizer.Tracking.application.service.unified;
using AdhdTimeOrganizer.Tracking.application.validator.unified;
using AdhdTimeOrganizer.Tracking.domain.helper.unified;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.unified.query;

/// <summary>
/// The merged filter: which trackers have anything to say about this span, how much of it each was
/// credited with, and how much each lost to another.
///
/// <para><b>This endpoint reads all three ledgers whatever the selection is</b>, because
/// <c>hasData</c> is deliberately independent of both selection and displacement. A source whose every
/// second was displaced still has data, and so does one the user has switched off — that is how the
/// filter can tell someone there is phone data they are not looking at, rather than presenting it as
/// "not connected".</para>
/// </summary>
public class UnifiedSourcesEndpoint(DbContext db, IUserTimeZoneResolver timeZones)
    : Endpoint<UnifiedDashboardRequest, List<UnifiedSourceStatusDto>>
{
    public override void Configure()
    {
        Post("/activity-tracking/unified/sources");
        Validator<UnifiedSourcesValidator>();
        Summary(s =>
        {
            s.Summary = "Get per-tracker standing for a merged span";
            s.Description =
                "For each of the three trackers: whether it recorded anything in the span at all, how " +
                "many seconds it was credited with after the overlap rule, how many it lost to another " +
                "source, and which source took them.";
            s.Response<List<UnifiedSourceStatusDto>>(200, "Success");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(UnifiedDashboardRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        var windows = req.ToDailyWindows(await timeZones.GetAsync(userId, ct));
        var selected = req.SelectedSources();

        var loads = await UnifiedActivityLoader.LoadAsync(db, userId, windows, TrackingSourceNames.All, ct);
        var span = UnifiedSpan.From(loads, selected);

        var response = TrackingSourceNames.All
            .Select(source => new UnifiedSourceStatusDto
            {
                Source = source.ToWireName(),
                HasData = loads.FirstOrDefault(load => load.Source == source)?.HasData ?? false,
                // A deselected source takes no part: it was never merged, so both figures are zero and
                // it appears in no item's `sources` array.
                CountedSeconds = selected.Contains(source) ? span.Ledger.CountedSeconds[source] : 0,
                DisplacedSeconds = selected.Contains(source) ? span.Ledger.DisplacedSeconds[source] : 0,
                DisplacedTo = selected.Contains(source) ? span.Ledger.DisplacedTo[source] : null
            })
            .ToList();

        await Send.ResponseAsync(response, cancellation: ct);
    }
}
