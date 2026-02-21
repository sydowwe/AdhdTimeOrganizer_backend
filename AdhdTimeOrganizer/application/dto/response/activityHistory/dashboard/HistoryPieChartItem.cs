namespace AdhdTimeOrganizer.application.dto.response.activityHistory.dashboard;

public record HistoryPieChartItem
{
    public string Name { get; set; } = string.Empty;
    public long TotalSeconds { get; set; }
    public string? Color { get; set; }
    public int Entries { get; set; }
}
