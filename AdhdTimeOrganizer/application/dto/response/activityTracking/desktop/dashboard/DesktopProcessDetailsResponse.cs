namespace AdhdTimeOrganizer.application.dto.response.activityTracking.desktop.dashboard;

public record DesktopProcessDetailsResponse
{
    public required string ProcessName { get; set; }
    public string? ProductName { get; set; }
    public int TotalSeconds { get; set; }
    public int ActiveSeconds { get; set; }
    public int BackgroundSeconds { get; set; }
    public int FullscreenSeconds { get; set; }
    public int SoundSeconds { get; set; }
    public List<DesktopMonitorUsageDto> MonitorBreakdown { get; set; } = [];
    public int Entries { get; set; }
    public List<DesktopWindowTitleVisitDto> WindowTitles { get; set; } = [];
}

public record DesktopWindowTitleVisitDto
{
    public string? WindowTitle { get; set; }
    public int TotalSeconds { get; set; }
    public int ActiveSeconds { get; set; }
    public int BackgroundSeconds { get; set; }
    public int FullscreenSeconds { get; set; }
    public int SoundSeconds { get; set; }
}

public record DesktopMonitorUsageDto
{
    public int Monitor { get; set; }
    public int ActiveSeconds { get; set; }
}
