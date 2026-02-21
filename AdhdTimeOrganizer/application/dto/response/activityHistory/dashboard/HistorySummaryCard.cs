namespace AdhdTimeOrganizer.application.dto.response.activityHistory.dashboard;

public record HistorySummaryCard
{
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public long TotalSeconds { get; set; }
    public long AverageSeconds { get; set; }
    public double? PercentChange { get; set; }
    public bool IsNew { get; set; }
}
