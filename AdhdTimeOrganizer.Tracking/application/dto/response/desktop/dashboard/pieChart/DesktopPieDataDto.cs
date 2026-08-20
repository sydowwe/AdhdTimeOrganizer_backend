namespace AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.desktop.dashboard.pieChart;

public record DesktopPieDataDto : IDashboardItem
{
    public required string ProcessName { get; init; }
    public string? ProductName { get; init; }

    /// <inheritdoc />
    public string Key => ProcessName;

    /// <inheritdoc />
    public string Label => DashboardItem.LabelOr(ProductName, ProcessName);

    public required int ActiveSeconds { get; init; }
    public required int BackgroundSeconds { get; init; }
    public required int TotalSeconds { get; init; }
    public required List<string> WindowTitles { get; init; }
    public required int Entries { get; init; }
}