using AdhdTimeOrganizer.History.application.dto.@enum;
using AdhdTimeOrganizer.History.application.dto.response.activityHistory.dashboard;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;

namespace AdhdTimeOrganizer.History.application.dashboard;

/// <summary>
/// The one place the pie chart's shape is decided: grouping, the 1% threshold, the <c>_other</c> roll-up
/// and the totals block.
/// </summary>
/// <remarks>
/// The <c>detail/</c> and <c>summary/</c> pie-chart endpoints differ only in how they turn their request
/// into a <c>(from, to)</c> UTC range — <c>ToDateTimeRange</c> vs <c>ToUtcRange</c>. Everything after the
/// query is identical, so it lives here: a fix to the threshold or the roll-up lands on both dashboards
/// at once instead of shipping half-done.
/// </remarks>
public static class HistoryPieChartBuilder
{
    /// <summary>The name of the synthetic slice the tail of the distribution is rolled up into.</summary>
    public const string OtherName = "_other";

    /// <summary>The colour <c>_other</c> renders with.</summary>
    public const string OtherColor = "#999";

    /// <summary>A group below this share of the period's total is rolled into <c>_other</c>.</summary>
    private const double MinPercentThreshold = 1.0;

    /// <summary>Slices returned when the request does not ask for a specific count.</summary>
    public const int DefaultMaxItems = 20;

    /// <summary>
    /// <paramref name="records"/> must already be user-scoped and range-filtered, with <c>Activity</c>,
    /// <c>Activity.Role</c> and <c>Activity.Category</c> loaded — see <see cref="HistoryGrouping"/>.
    /// </summary>
    /// <param name="maxItems">
    /// The cap on returned slices, <c>_other</c> included; non-positive falls back to
    /// <see cref="DefaultMaxItems"/>. At most <c>maxItems - 1</c> real groups are returned so the roll-up
    /// always has room.
    /// </param>
    public static HistoryPieChartResponse Build(
        IReadOnlyCollection<ActivityHistory> records, HistoryGroupBy groupBy, int maxItems)
    {
        // Each group keeps its rows so the _other roll-up can count sittings over them, not sum the groups'.
        // Entries are sittings, not rows — see HistorySittings for why, and for why a session split across
        // two groups counts once in each.
        var allGroups = records
            .GroupBy(ah => ah.ResolveGroupKey(groupBy))
            .Select(g => (Item: new HistoryPieChartItem
            {
                GroupId = g.Key.Id,
                Name = g.Key.Name,
                TotalSeconds = g.Sum(ah => ah.Length.TotalSeconds),
                Color = g.Key.Color,
                Entries = g.CountSittings()
            }, Rows: g))
            .OrderByDescending(g => g.Item.TotalSeconds)
            .ToList();

        var grandTotal = allGroups.Sum(g => g.Item.TotalSeconds);
        var cap = maxItems > 0 ? maxItems : DefaultMaxItems;

        var items = new List<HistoryPieChartItem>();

        if (grandTotal > 0)
        {
            var threshold = grandTotal * MinPercentThreshold / 100.0;

            var aboveThreshold = allGroups.Where(g => g.Item.TotalSeconds >= threshold).ToList();
            var belowThreshold = allGroups.Where(g => g.Item.TotalSeconds < threshold).ToList();

            // Take at most (cap - 1) items above threshold to leave room for "other"
            items = aboveThreshold.Take(cap - 1).Select(g => g.Item).ToList();

            // Everything that didn't make the final list: the above-threshold tail cut by the cap, then
            // all of the below-threshold groups.
            var remaining = aboveThreshold.Skip(cap - 1).Concat(belowThreshold).ToList();

            if (remaining.Count > 0)
                items.Add(new HistoryPieChartItem
                {
                    // A roll-up of many groups, so it is no single entity: null id, keyed by name.
                    GroupId = null,
                    Name = OtherName,
                    TotalSeconds = remaining.Sum(g => g.Item.TotalSeconds),
                    Color = OtherColor,
                    // Counted over the rolled-up rows, so a session spanning two tail groups is one entry here.
                    Entries = remaining.SelectMany(g => g.Rows).CountSittings()
                });
        }

        return new HistoryPieChartResponse
        {
            Items = items,
            Totals = new HistoryPieTotals
            {
                TotalSeconds = grandTotal,
                TotalEntries = records.CountSittings(),
                UniqueGroups = allGroups.Count
            }
        };
    }
}
