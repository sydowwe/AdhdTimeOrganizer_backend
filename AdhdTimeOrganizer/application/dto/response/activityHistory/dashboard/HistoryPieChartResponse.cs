namespace AdhdTimeOrganizer.application.dto.response.activityHistory.dashboard;

public record HistoryPieChartResponse
{
    public required List<HistoryPieChartItem> Items { get; set; }
    public required HistoryPieTotals Totals { get; init; }
}
