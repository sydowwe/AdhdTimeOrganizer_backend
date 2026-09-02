using AdhdTimeOrganizer.History.application.dto.@enum;
using AdhdTimeOrganizer.History.application.dto.response.activityHistory.dashboard;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;

namespace AdhdTimeOrganizer.History.application.dashboard;

/// <summary>
/// The one place a summary-cards response is assembled: the top-N cards, their baseline comparison and the
/// period-over-period total.
/// </summary>
/// <remarks>
/// The <c>detail/</c> and <c>summary/</c> summary-cards endpoints differ only in how they turn their request
/// into a <c>(from, to)</c> UTC range — <c>ToDateTimeRange</c> vs <c>ToUtcRange</c>. Everything after the
/// queries is identical, so it lives here rather than being maintained twice.
/// </remarks>
public static class HistorySummaryCardsBuilder
{
    /// <summary>
    /// All three record sets must already be user-scoped and range-filtered; the two compared against the
    /// cards need <c>Activity</c>, <c>Activity.Role</c> and <c>Activity.Category</c> loaded — see
    /// <see cref="HistoryGrouping"/>.
    /// </summary>
    /// <param name="currentRecords">Rows in the reported period.</param>
    /// <param name="baselineRecords">
    /// Rows in <paramref name="window"/>'s range, unfiltered — the weekday filter is applied here.
    /// </param>
    /// <param name="window">The resolved baseline, from <see cref="HistoryBaselineWindow.Resolve"/>.</param>
    /// <param name="dayCount">Days the reported period spans, for the current daily average.</param>
    /// <param name="previousPeriodTotalSeconds">
    /// Total of the period of the same length immediately before this one, for the period comparison.
    /// </param>
    public static HistorySummaryCardsResponse Build(
        IReadOnlyCollection<ActivityHistory> currentRecords,
        List<ActivityHistory> baselineRecords,
        HistoryBaselineWindow window,
        HistoryGroupBy groupBy,
        int topN,
        int dayCount,
        long previousPeriodTotalSeconds)
    {
        var topGroups = currentRecords
            .GroupBy(ah => ah.ResolveGroupKey(groupBy))
            .Select(g => new
            {
                g.Key,
                TotalSeconds = g.Sum(ah => (long)ah.Length.TotalSeconds)
            })
            .OrderByDescending(g => g.TotalSeconds)
            .Take(topN)
            .ToList();

        var baseline = window.Filter(baselineRecords);
        var baselineDays = window.DayCount(baseline);

        var cards = topGroups.Select(g =>
        {
            // Matched on the entity id, not the display name — so a group renamed between the baseline
            // period and this one still finds its own past, instead of reading as IsNew.
            var baselineTotal = baseline
                .Where(ah => ah.ResolveGroupKey(groupBy).Id == g.Key.Id)
                .Sum(ah => (long)ah.Length.TotalSeconds);

            var baselineDailyAvg = baselineDays > 0 ? baselineTotal / baselineDays : 0;
            var currentDailyAvg = dayCount > 0 ? g.TotalSeconds / dayCount : g.TotalSeconds;

            var isNew = baselineTotal == 0 && g.TotalSeconds > 0;

            double? percentChange = null;
            if (!isNew && baselineDailyAvg > 0)
                percentChange = Math.Round((double)(currentDailyAvg - baselineDailyAvg) / baselineDailyAvg * 100, 1);

            return new HistorySummaryCard
            {
                GroupId = g.Key.Id,
                Name = g.Key.Name,
                Color = g.Key.Color,
                TotalSeconds = g.TotalSeconds,
                AverageSeconds = baselineDailyAvg,
                PercentChange = percentChange,
                IsNew = isNew
            };
        }).ToList();

        var currentTotal = currentRecords.Sum(ah => (long)ah.Length.TotalSeconds);

        double? periodPercentChange = null;
        if (previousPeriodTotalSeconds > 0)
            periodPercentChange = Math.Round(
                (double)(currentTotal - previousPeriodTotalSeconds) / previousPeriodTotalSeconds * 100, 1);

        return new HistorySummaryCardsResponse
        {
            Cards = cards,
            PeriodComparison = new HistoryPeriodComparison
            {
                PreviousPeriodTotalSeconds = previousPeriodTotalSeconds,
                CurrentPeriodTotalSeconds = currentTotal,
                PercentChange = periodPercentChange
            }
        };
    }
}
