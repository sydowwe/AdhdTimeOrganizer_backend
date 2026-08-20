namespace AdhdTimeOrganizer.History.application.dto.response.activityHistory.dashboard;

public record HistoryPieChartItem
{
    /// <summary>
    /// The id of the entity this slice was grouped by — the activity, role or category id, per the
    /// request's <c>GroupBy</c>. Null only on the two synthetic slices that are not an entity: the
    /// <c>Uncategorized</c> bucket and the <c>_other</c> roll-up. See <c>HistoryGroupKey</c>.
    /// </summary>
    public long? GroupId { get; set; }

    public string Name { get; set; } = string.Empty;
    public long TotalSeconds { get; set; }
    public string? Color { get; set; }
    public int Entries { get; set; }
}