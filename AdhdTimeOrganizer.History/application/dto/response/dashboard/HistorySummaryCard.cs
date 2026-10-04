namespace AdhdTimeOrganizer.History.application.dto.response.activityHistory.dashboard;

public record HistorySummaryCard
{
    /// <summary>
    /// The id of the entity this card was grouped by, per the request's <c>GroupBy</c>. Null only on the
    /// <c>Uncategorized</c> bucket. It is also what <c>PercentChange</c> / <c>IsNew</c> match the baseline
    /// period on, so a renamed activity now compares against itself. See <c>HistoryGroupKey</c>.
    /// </summary>
    public long? GroupId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public long TotalSeconds { get; set; }

    /// <summary>
    /// The baseline's mean day scaled to the reported period's length — what <see cref="TotalSeconds"/> would
    /// be on a usual stretch of the same length, and the number <see cref="PercentChange"/> is computed
    /// against. On a one-day period it is the baseline's daily average. 0 when the baseline found nothing.
    /// </summary>
    public long AverageSeconds { get; set; }
    public double? PercentChange { get; set; }
    public bool IsNew { get; set; }
}