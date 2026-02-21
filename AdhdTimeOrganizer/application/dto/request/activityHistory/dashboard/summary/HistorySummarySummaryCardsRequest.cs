namespace AdhdTimeOrganizer.application.dto.request.activityHistory.dashboard.summary;

public record HistorySummarySummaryCardsRequest : HistorySummaryDateRangeRequest
{
    public string Baseline { get; init; } = "last7days";
    public int TopN { get; init; } = 4;
}
