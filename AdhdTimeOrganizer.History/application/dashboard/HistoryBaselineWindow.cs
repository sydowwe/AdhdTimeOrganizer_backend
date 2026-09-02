using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;

namespace AdhdTimeOrganizer.History.application.dashboard;

/// <summary>
/// The stretch of time a summary card's <c>averageSeconds</c> / <c>percentChange</c> is measured against,
/// resolved from the request's <c>baseline</c> string.
/// </summary>
/// <remarks>
/// Resolved before the baseline query runs, because <see cref="From"/> and <see cref="To"/> are its range;
/// <see cref="WeekdayFilter"/> and <see cref="DayCount"/> are applied to the rows it returns. Both
/// summary-cards endpoints resolve it the same way and differ only in how they derive the current period.
/// </remarks>
public readonly record struct HistoryBaselineWindow
{
    private HistoryBaselineWindow(DateTime from, DateTime to, string baseline, HashSet<DayOfWeek>? weekdayFilter)
    {
        From = from;
        To = to;
        Baseline = baseline;
        WeekdayFilter = weekdayFilter;
    }

    /// <summary>Inclusive start of the baseline range. <c>DateTime.MinValue</c> for <c>allTime</c>.</summary>
    public DateTime From { get; }

    /// <summary>Exclusive end — always the current period's start, so the two never overlap.</summary>
    public DateTime To { get; }

    /// <summary>The request's baseline string, kept so <see cref="DayCount"/> can branch on it.</summary>
    public string Baseline { get; }

    /// <summary>
    /// For <c>sameWeekday</c>, the weekdays the current period covers; null for every other baseline.
    /// Baseline rows falling on any other weekday are dropped by <see cref="Filter"/>.
    /// </summary>
    public HashSet<DayOfWeek>? WeekdayFilter { get; }

    /// <param name="currentFrom">Start of the period being reported on; the baseline ends here.</param>
    /// <param name="dateFrom">The current period's first day, on the user's clock.</param>
    /// <param name="dateTo">The current period's last day, on the user's clock.</param>
    /// <param name="baseline">Case-insensitive; anything unrecognised falls back to <c>last7days</c>.</param>
    public static HistoryBaselineWindow Resolve(
        DateTime currentFrom, DateOnly dateFrom, DateOnly dateTo, string baseline)
    {
        return baseline.ToLowerInvariant() switch
        {
            "last7days" => new HistoryBaselineWindow(currentFrom.AddDays(-7), currentFrom, baseline, null),
            "last30days" => new HistoryBaselineWindow(currentFrom.AddDays(-30), currentFrom, baseline, null),
            "sameweekday" => new HistoryBaselineWindow(
                currentFrom.AddDays(-56), currentFrom, baseline, GetWeekdaysInRange(dateFrom, dateTo)),
            "alltime" => new HistoryBaselineWindow(DateTime.MinValue, currentFrom, baseline, null),
            _ => new HistoryBaselineWindow(currentFrom.AddDays(-7), currentFrom, baseline, null)
        };
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
        switch (Baseline.ToLowerInvariant())
        {
            case "last30days":
                return 30;
            case "sameweekday":
                return 8 * (WeekdayFilter?.Count ?? 1);
            case "alltime":
                if (filteredRecords.Count == 0)
                    return 1;
                var minDate = filteredRecords.Min(ah => ah.StartTimestamp).Date;
                var days = (long)(To.Date - minDate).TotalDays;
                return days > 0 ? days : 1;
            default:
                return 7;
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
