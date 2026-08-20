namespace AdhdTimeOrganizer.History.application.dto.response.activityHistory.dashboard;

public record HistoryGroupItem
{
    /// <summary>
    /// The id of the entity this segment was grouped by, per the request's <c>GroupBy</c>. Null only on
    /// the <c>Uncategorized</c> bucket. See <c>HistoryGroupKey</c>.
    /// </summary>
    public long? GroupId { get; set; }

    public string Name { get; set; } = string.Empty;
    public long TotalSeconds { get; set; }
    public string? Color { get; set; }
}