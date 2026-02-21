namespace AdhdTimeOrganizer.application.dto.request.activityHistory.dashboard.detail;

public record HistoryDetailSummaryCardsRequest : HistoryDetailDateRangeRequest
{
    public string Baseline { get; init; } = "last7days";
    public int TopN { get; init; } = 4;
}
