namespace AdhdTimeOrganizer.application.dto.response.activityTracking.desktop.dashboard.pieChart;

public record DesktopPieChartResponse
{
    public required List<DesktopPieDataDto> Processes { get; set; }
    public required DesktopDayTotalsResponse Totals { get; init; }
}