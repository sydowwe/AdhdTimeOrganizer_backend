namespace AdhdTimeOrganizer.application.dto.response.activityHistory.dashboard;

public record HistoryWindow
{
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
    public List<HistoryGroupItem> Items { get; set; } = new();
}
