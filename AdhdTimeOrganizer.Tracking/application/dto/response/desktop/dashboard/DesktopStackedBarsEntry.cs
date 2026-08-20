namespace AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.desktop.dashboard;

public record DesktopStackedBarsEntry : IDashboardItem
{
    public required string ProcessName { get; set; }
    public string? ProductName { get; set; }

    /// <inheritdoc />
    public string Key => ProcessName;

    /// <inheritdoc />
    public string Label => DashboardItem.LabelOr(ProductName, ProcessName);

    public int ActiveSeconds { get; set; }
    public int BackgroundSeconds { get; set; }
    public int TotalSeconds => ActiveSeconds + BackgroundSeconds;
}