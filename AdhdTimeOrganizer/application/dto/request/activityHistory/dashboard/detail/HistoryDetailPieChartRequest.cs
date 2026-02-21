namespace AdhdTimeOrganizer.application.dto.request.activityHistory.dashboard.detail;

public record HistoryDetailPieChartRequest : HistoryDetailDateRangeRequest
{
    public int MaxItems { get; init; } = 20;
}
