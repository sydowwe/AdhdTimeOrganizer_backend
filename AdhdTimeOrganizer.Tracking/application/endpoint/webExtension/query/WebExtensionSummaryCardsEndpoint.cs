using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.summaryCards;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.helper;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.webExtension.query;

/// <summary>
/// Top browsed domains over the span, each against the user's own recent self. Everything the card
/// means — the lookback, the per-day window applied to it, the weekday fold and the scaling — is
/// <see cref="BaseSummaryCardsEndpoint{TRequest,TItem}"/>'s; this class is the web-extension ledger's
/// read and its DTO.
/// </summary>
public class WebExtensionSummaryCardsEndpoint(DbContext db, IUserTimeZoneResolver timeZones)
    : BaseSummaryCardsEndpoint<SummaryCardsRequest, DomainSummaryDto>(timeZones)
{
    protected override int DefaultTopN => 5;

    public override void Configure() =>
        ConfigureSummaryCards<TopDomainsValidator>(
            "/activity-tracking/web-extension/summary-cards",
            "Get top domain summary cards with baseline comparison",
            "Returns summary statistics for top domains with baseline comparison against a selected reference period");

    protected override async Task<IReadOnlyList<SummaryRow>> LoadAsync(
        SummaryCardsRequest req, long userId, DailyWindowSet windows, CancellationToken ct)
    {
        var from = windows.EnvelopeFrom;
        var to = windows.EnvelopeTo;

        var rows = await db.Set<WebExtensionActivityEntry>()
            .Where(x => x.UserId == userId)
            .Where(x => x.WindowStart >= from && x.WindowStart < to)
            .ToListAsync(ct);

        return windows.Restrict(rows, x => x.WindowStart)
            .Select(x => new SummaryRow(x.Domain, null, x.WindowStart, x.ActiveSeconds, x.BackgroundSeconds))
            .ToList();
    }

    protected override DomainSummaryDto BuildItem(SummaryItem current, SummaryBaseline? baseline) =>
        new()
        {
            Domain = current.Key,
            IsNew = baseline is null,

            Active = current.ActiveSeconds > 0
                ? Stat(current.ActiveSeconds, baseline?.ActiveSeconds)
                : null,

            Background = current.BackgroundSeconds > 0
                ? Stat(current.BackgroundSeconds, baseline?.BackgroundSeconds)
                : null
        };

    private static ActivityStatDto Stat(long seconds, long? average) => new()
    {
        Seconds = (int)seconds,
        AverageSeconds = average is null ? null : (int)average.Value,
        PercentChange = average is null or 0 ? null : PercentChange(seconds, average.Value)
    };
}
