namespace AdhdTimeOrganizer.application.dto.response.activityTracking.desktop;

public record DesktopPieChartResponse
{
    public required List<DesktopPieDataDto> Processes { get; set; }
    public required DesktopDayTotalsResponse Totals { get; init; }
}

public record DesktopDayTotalsResponse
{
    public int TotalSeconds { get; set; }
    public int ActiveSeconds { get; set; }
    public int BackgroundSeconds { get; set; }
    public int TotalProcesses { get; set; }
    public int TotalWindowTitles { get; set; }
    public int TotalEntries { get; set; }
}
