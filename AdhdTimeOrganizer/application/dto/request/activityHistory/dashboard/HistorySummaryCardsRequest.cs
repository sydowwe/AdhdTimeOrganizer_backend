namespace AdhdTimeOrganizer.application.dto.request.activityHistory.dashboard;

public record HistorySummaryCardsRequest : HistoryDateRangeRequest
{
    public string Baseline { get; init; } = "last7days";
    public int TopN { get; init; } = 4;
}
