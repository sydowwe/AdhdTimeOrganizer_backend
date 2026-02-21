namespace AdhdTimeOrganizer.application.dto.response.activityHistory.dashboard;

public record HistorySummaryCardsResponse
{
    public required List<HistorySummaryCard> Cards { get; set; }
    public required HistoryPeriodComparison PeriodComparison { get; init; }
}
