namespace AdhdTimeOrganizer.application.dto.response.activityTracking.pieChart;

public record PieChartResponse
{
    public required List<DomainPieDataDto> Domains { get; set; }
    public required DayTotalsResponse Totals { get; init; }
}