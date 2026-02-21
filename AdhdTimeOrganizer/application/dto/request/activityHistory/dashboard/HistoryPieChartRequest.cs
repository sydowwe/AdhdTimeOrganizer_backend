namespace AdhdTimeOrganizer.application.dto.request.activityHistory.dashboard;

public record HistoryPieChartRequest : HistoryDateRangeRequest
{
    public int TopN { get; init; } = 20;
}
