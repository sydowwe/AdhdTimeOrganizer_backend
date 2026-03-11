namespace AdhdTimeOrganizer.application.dto.response.activityTracking.desktop.dashboard.pieChart;

public record DesktopDayTotalsResponse
{
    public int TotalSeconds { get; set; }
    public int ActiveSeconds { get; set; }
    public int BackgroundSeconds { get; set; }
    public int TotalProcesses { get; set; }
    public int TotalWindowTitles { get; set; }
    public int TotalEntries { get; set; }
}