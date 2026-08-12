namespace AdhdTimeOrganizer.History.application.dto.request.activityHistory.dashboard.summary;

public record HistorySummaryPieChartRequest : HistorySummaryDateRangeRequest
{
    public int MaxItems { get; init; } = 20;
}