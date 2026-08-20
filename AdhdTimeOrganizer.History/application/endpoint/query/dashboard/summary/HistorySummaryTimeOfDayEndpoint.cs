using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.History.application.dto.request.activityHistory.dashboard.summary;
using AdhdTimeOrganizer.History.application.dto.response.activityHistory.dashboard;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.History.application.endpoint.activityHistory.activityHistory.query.dashboard.summary;

/// <summary>
/// Folds the range into 24 hour-of-day buckets — the one aggregation none of the sibling <c>summary/</c>
/// endpoints exposes, and the reason this exists rather than the client folding an existing response.
///
/// <para><b>The range is selected exactly as <c>summary/pie-chart</c> selects it</b> — same
/// <see cref="Core.application.dto.dto.DateRangeDto.ToUtcRange"/> bounds, same <c>StartTimestamp</c>
/// predicate, and each record's whole <c>Length</c> is distributed. So <c>sum(Hours[].TotalSeconds)</c>
/// equals that endpoint's <c>Totals.TotalSeconds</c> for the same range, which is a property the frontend
/// is told it may rely on. Note what that implies at the far edge: a record starting inside the range
/// contributes all of its length, including any minutes that run past the range's last midnight. Clipping
/// the tail instead would be defensible on its own but would break the equality.</para>
/// </summary>
public class HistorySummaryTimeOfDayEndpoint(DbContext db, IUserTimeZoneResolver timeZones)
    : Endpoint<HistorySummaryTimeOfDayRequest, HistoryTimeOfDayResponse>
{
    private const int HoursInDay = 24;
    private const int SecondsInHour = 3600;

    public override void Configure()
    {
        Post("/activity-history/dashboard/summary/time-of-day");
    }

    public override async Task HandleAsync(HistorySummaryTimeOfDayRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        // The user's days, on the user's clock — the fold below is meaningless in any other zone.
        var timeZone = await timeZones.GetAsync(userId, ct);
        var (fromDate, toDate) = req.ToDateRange();
        var (from, to) = req.ToUtcRange(timeZone);

        // No Includes: nothing here groups by activity, role or category, so the two joins every other
        // dashboard needs would be read and thrown away. The (UserId, StartTimestamp) index covers this.
        var records = await db.Set<ActivityHistory>()
            .Where(ah => ah.UserId == userId)
            .Where(ah => ah.StartTimestamp >= from && ah.StartTimestamp < to)
            .Select(ah => new { ah.StartTimestamp, ah.Length })
            .ToListAsync(ct);

        var totalSeconds = new long[HoursInDay];
        var entries = new int[HoursInDay];
        var touched = new bool[HoursInDay];
        var daysWithActivity = new HashSet<DateOnly>();

        foreach (var record in records)
        {
            daysWithActivity.Add(DateOnly.FromDateTime(WallClockZone.FromUtc(record.StartTimestamp, timeZone)));

            Array.Clear(touched);
            Distribute(record.StartTimestamp, record.Length.TotalSeconds, timeZone, totalSeconds, entries, touched);
        }

        var response = new HistoryTimeOfDayResponse
        {
            Hours = Enumerable.Range(0, HoursInDay)
                .Select(hour => new HistoryTimeOfDayHour
                {
                    Hour = hour,
                    TotalSeconds = totalSeconds[hour],
                    Entries = entries[hour]
                })
                .ToList(),
            DaysInRange = toDate.DayNumber - fromDate.DayNumber + 1,
            DaysWithActivity = daysWithActivity.Count
        };

        await Send.ResponseAsync(response, cancellation: ct);
    }

    /// <summary>
    /// Credits one record's seconds to every hour of day it covers, split by elapsed time: 90 minutes from
    /// 09:45 is 900s in hour 9 and 4500s in hour 10.
    ///
    /// <para>Attributing the whole record to its start hour would be cheaper and answers a different
    /// question — <i>when do you start things</i> — which reads as plausible on screen and is not what the
    /// chart claims to show.</para>
    ///
    /// <para>The walk re-derives the wall clock from the instant on every step rather than adding hours to a
    /// local <see cref="DateTime"/>, so a record running across a DST transition is still credited to the
    /// hours the user's clock actually read: on the fall-back night the repeated 02:00 hour is credited
    /// twice, which is what happened. A zero-length record credits its start hour with no seconds and one
    /// entry, so it stays visible to a client counting contributors without moving any total.</para>
    /// </summary>
    private static void Distribute(
        DateTime startUtc, long lengthSeconds, TimeZoneInfo timeZone,
        long[] totalSeconds, int[] entries, bool[] touched)
    {
        var instant = startUtc;
        var remaining = Math.Max(lengthSeconds, 0);

        do
        {
            var local = WallClockZone.FromUtc(instant, timeZone);
            var hour = local.Hour;

            var take = Math.Min(remaining, SecondsInHour - (local.Minute * 60 + local.Second));

            totalSeconds[hour] += take;

            // Once per hour touched, so a record spanning a boundary is one contributor to each hour it
            // covers — summing Entries across the 24 buckets is therefore not the period's record count.
            if (!touched[hour])
            {
                touched[hour] = true;
                entries[hour]++;
            }

            remaining -= take;
            instant = instant.AddSeconds(take);
        } while (remaining > 0);
    }
}
