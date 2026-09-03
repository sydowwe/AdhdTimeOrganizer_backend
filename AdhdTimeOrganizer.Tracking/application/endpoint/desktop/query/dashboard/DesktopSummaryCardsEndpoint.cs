using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.desktop.dashboard;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.summaryCards;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.helper;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.desktop.query.dashboard;

/// <summary>
/// Top desktop processes over the span, each against the user's own recent self. Everything the card
/// means — the lookback, the per-day window applied to it, the weekday fold and the scaling — is
/// <see cref="BaseSummaryCardsEndpoint{TRequest,TItem}"/>'s; this class is the desktop ledger's read and
/// the desktop DTO.
/// </summary>
public class DesktopSummaryCardsEndpoint(DbContext db, IUserTimeZoneResolver timeZones)
    : BaseSummaryCardsEndpoint<SummaryCardsRequest, DesktopProcessSummaryDto>(timeZones)
{
    protected override int DefaultTopN => 5;

    public override void Configure() =>
        ConfigureSummaryCards<TopDomainsValidator>(
            "/activity-tracking/desktop/summary-cards",
            "Get desktop process usage summary cards",
            "Returns top N processes with current usage (active and background) and comparison against baseline period");

    protected override async Task<IReadOnlyList<SummaryRow>> LoadAsync(
        SummaryCardsRequest req, long userId, DailyWindowSet windows, CancellationToken ct)
    {
        var from = windows.EnvelopeFrom;
        var to = windows.EnvelopeTo;

        var rows = await db.Set<DesktopActivityEntry>()
            .Where(x => x.UserId == userId)
            .Where(x => x.WindowStart >= from && x.WindowStart < to)
            .ToListAsync(ct);

        return windows.Restrict(rows, x => x.WindowStart)
            .Select(x => new SummaryRow(
                x.ProcessName, x.ProductName, x.WindowStart, x.ActiveSeconds, x.BackgroundSeconds))
            .ToList();
    }

    protected override DesktopProcessSummaryDto BuildItem(SummaryItem current, SummaryBaseline? baseline) =>
        new()
        {
            ProcessName = current.Key,
            // The product name is the friendlier label; falling back to the process name keeps the card
            // from rendering an empty title for an executable that declares none.
            ProductName = current.Detail ?? current.Key,
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
