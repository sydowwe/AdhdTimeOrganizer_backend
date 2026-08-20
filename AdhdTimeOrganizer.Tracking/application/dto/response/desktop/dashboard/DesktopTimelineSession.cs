namespace AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.desktop.dashboard;

public record DesktopTimelineSession : IDashboardItem
{
    public required long Id { get; set; }
    public required string ProcessName { get; set; }
    public string? ProductName { get; set; }

    /// <inheritdoc />
    public string Key => ProcessName;

    /// <inheritdoc />
    public string Label => DashboardItem.LabelOr(ProductName, ProcessName);

    public required DateTime StartedAt { get; set; }
    public required DateTime EndedAt { get; set; }
    public required int DurationSeconds { get; set; }
    public required int TotalSeconds { get; set; }
}