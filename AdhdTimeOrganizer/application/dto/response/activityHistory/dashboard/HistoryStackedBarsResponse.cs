namespace AdhdTimeOrganizer.application.dto.response.activityHistory.dashboard;

public record HistoryStackedBarsResponse
{
    public List<HistoryWindow> Windows { get; set; } = new();
}
