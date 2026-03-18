namespace AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;

public sealed record DesktopActivityEntryDto
{
    public required string ProcessName { get; init; }
    public string? ProductName { get; init; }

    public string? WindowTitle { get; init; }
    public string? ExecutablePath { get; init; }


    public required bool IsFullscreen { get; init; }
    public required int ActiveSeconds { get; init; }
    public required int BackgroundSeconds { get; init; }
    public required bool IsPlayingSound { get; init; }
    public required int ActiveMonitor { get; init; }
}
