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

    // No window-title list. It was every distinct title of the process over the whole span, which no client
    // renders (PERF-5). The ranked, timed list is GET …/process-details, and the count is
    // DesktopDayTotalsResponse.TotalWindowTitles.

    public required int Entries { get; init; }
}