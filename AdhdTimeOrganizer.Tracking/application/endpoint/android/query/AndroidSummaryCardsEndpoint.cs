using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.android.dashboard;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.helper;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.android.query;

/// <summary>
/// Top android apps over the span, each against the user's own recent self. Everything the card means —
/// the lookback, the per-day window applied to it, the weekday fold and the scaling — is
/// <see cref="BaseSummaryCardsEndpoint{TRequest,TItem}"/>'s; this class is the session ledger's read and
/// the android DTO.
///
/// <para>A session is a single duration, so it lands in the shared row's active seconds and the
/// background half stays zero: this ledger records no background time, which is also why the card
/// carries one stat rather than the two its siblings do.</para>
/// </summary>
public class AndroidSummaryCardsEndpoint(DbContext db, IUserTimeZoneResolver timeZones)
    : BaseSummaryCardsEndpoint<SummaryCardsRequest, AndroidAppSummaryDto>(timeZones)
{
    public override void Configure() =>
        ConfigureSummaryCards<TopDomainsValidator>(
            "/activity-tracking/android/summary-cards",
            "Get Android app usage summary cards",
            "Returns top N apps with current usage and comparison against baseline period");

    protected override async Task<IReadOnlyList<SummaryRow>> LoadAsync(
        SummaryCardsRequest req, long userId, DailyWindowSet windows, CancellationToken ct)
    {
        var from = windows.EnvelopeFrom;
        var to = windows.EnvelopeTo;

        var rows = await db.Set<AndroidSessionData>()
            .Where(x => x.UserId == userId)
            .Where(x => x.SessionStartUtc >= from && x.SessionStartUtc < to)
            .ToListAsync(ct);

        return windows.Restrict(rows, x => x.SessionStartUtc)
            .Select(x => new SummaryRow(x.AppLabel, x.PackageName, x.SessionStartUtc, x.DurationSeconds, 0))
            .ToList();
    }

    protected override AndroidAppSummaryDto BuildItem(SummaryItem current, SummaryBaseline? baseline) =>
        new()
        {
            PackageName = current.Detail ?? current.Key,
            AppLabel = current.Key,
            IsNew = baseline is null,
            Stat = new AndroidActivityStatDto
            {
                Seconds = current.ActiveSeconds,
                AverageSeconds = baseline?.ActiveSeconds,
                PercentChange = baseline is null || baseline.ActiveSeconds == 0
                    ? null
                    : PercentChange(current.ActiveSeconds, baseline.ActiveSeconds)
            }
        };
}
