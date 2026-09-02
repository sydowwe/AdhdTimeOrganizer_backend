using AdhdTimeOrganizer.History.application.dto.@enum;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;

namespace AdhdTimeOrganizer.History.application.dashboard;

/// <summary>
/// The stretch of time a summary card's <c>averageSeconds</c> / <c>percentChange</c> is measured against,
/// resolved from the request's <see cref="HistoryBaseline"/>.
/// </summary>
/// <remarks>
/// Resolved before the baseline query runs, because <see cref="From"/> and <see cref="To"/> are its range;
/// <see cref="WeekdayFilter"/> and <see cref="DayCount"/> are applied to the rows it returns. Both
/// summary-cards endpoints resolve it the same way and differ only in how they derive the current period.
/// </remarks>
public readonly record struct HistoryBaselineWindow
{
    private HistoryBaselineWindow(
        DateTime? from, DateTime to, HistoryBaseline baseline, HashSet<DayOfWeek>? weekdayFilter)
    {
        From = from;
        To = to;
        Baseline = baseline;
        WeekdayFilter = weekdayFilter;
    }

    /// <summary>
    /// Inclusive start of the baseline range, or null for <c>allTime</c>, which has no lower bound.
    /// Apply it with <see cref="ApplyRange"/> rather than reading it — a sentinel here would be an
    /// <c>Unspecified</c>-kind <see cref="DateTime"/>, which Npgsql refuses to write to a
    /// <c>timestamptz</c> column.
    /// </summary>
    public DateTime? From { get; }

    /// <summary>Exclusive end — always the current period's start, so the two never overlap.</summary>
    public DateTime To { get; }

    /// <summary>The request's baseline, kept so <see cref="DayCount"/> can branch on it.</summary>
    public HistoryBaseline Baseline { get; }

    /// <summary>
    /// For <c>sameWeekday</c>, the weekdays the current period covers; null for every other baseline.
    /// Baseline rows falling on any other weekday are dropped by <see cref="Filter"/>.
    /// </summary>
    public HashSet<DayOfWeek>? WeekdayFilter { get; }

    /// <param name="currentFrom">Start of the period being reported on; the baseline ends here.</param>
    /// <param name="dateFrom">The current period's first day, on the user's clock.</param>
    /// <param name="dateTo">The current period's last day, on the user's clock.</param>
    /// <param name="baseline">
    /// Exhaustive — there is no fallback arm. An unrecognised value cannot reach here (the binder rejects
    /// it), and a new <see cref="HistoryBaseline"/> member must be handled rather than silently reading as
    /// <see cref="HistoryBaseline.Last7Days"/>.
    /// </param>
    public static HistoryBaselineWindow Resolve(
        DateTime currentFrom, DateOnly dateFrom, DateOnly dateTo, HistoryBaseline baseline)
    {
        return baseline switch
        {
            HistoryBaseline.Last7Days => new HistoryBaselineWindow(currentFrom.AddDays(-7), currentFrom, baseline, null),
            HistoryBaseline.Last30Days => new HistoryBaselineWindow(currentFrom.AddDays(-30), currentFrom, baseline, null),
            HistoryBaseline.SameWeekday => new HistoryBaselineWindow(
                currentFrom.AddDays(-56), currentFrom, baseline, GetWeekdaysInRange(dateFrom, dateTo)),
            HistoryBaseline.AllTime => new HistoryBaselineWindow(null, currentFrom, baseline, null),
            _ => throw new ArgumentOutOfRangeException(
                nameof(baseline), baseline, "Unhandled baseline — add an arm rather than letting it fall back.")
        };
    }

    /// <summary>
    /// Narrows a baseline query to this window. <c>allTime</c> gets no lower bound at all, so the
    /// query stays a plain range scan on <c>(UserId, StartTimestamp)</c> instead of carrying a
    /// predicate that excludes nothing.
    /// </summary>
    public IQueryable<ActivityHistory> ApplyRange(IQueryable<ActivityHistory> query)
    {
        if (From is { } from)
            query = query.Where(ah => ah.StartTimestamp >= from);

        var to = To;
        return query.Where(ah => ah.StartTimestamp < to);
    }

    /// <summary>Drops the rows the window's range could not exclude — today, only the weekday filter.</summary>
    public List<ActivityHistory> Filter(List<ActivityHistory> baselineRecords)
    {
        var weekdays = WeekdayFilter;
        return weekdays == null
            ? baselineRecords
            : baselineRecords.Where(ah => weekdays.Contains(ah.StartTimestamp.DayOfWeek)).ToList();
    }

    /// <summary>
    /// Days the baseline total is divided by to get a daily average. Never zero, so the division is safe.
    /// </summary>
    /// <param name="filteredRecords">The rows <see cref="Filter"/> returned — only <c>allTime</c> reads them.</param>
    public long DayCount(IReadOnlyCollection<ActivityHistory> filteredRecords)
    {
        switch (Baseline)
        {
            case HistoryBaseline.Last7Days:
                return 7;
            case HistoryBaseline.Last30Days:
                return 30;
            case HistoryBaseline.SameWeekday:
                return 8 * (WeekdayFilter?.Count ?? 1);
            case HistoryBaseline.AllTime:
                if (filteredRecords.Count == 0)
                    return 1;
                var minDate = filteredRecords.Min(ah => ah.StartTimestamp).Date;
                var days = (long)(To.Date - minDate).TotalDays;
                return days > 0 ? days : 1;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(Baseline), Baseline, "Unhandled baseline — add an arm rather than dividing by 7.");
        }
    }

    private static HashSet<DayOfWeek> GetWeekdaysInRange(DateOnly dateFrom, DateOnly dateTo)
    {
        var weekdays = new HashSet<DayOfWeek>();
        var current = dateFrom;
        while (current <= dateTo)
        {
            weekdays.Add(current.DayOfWeek);
            current = current.AddDays(1);
        }

        return weekdays;
    }
}
