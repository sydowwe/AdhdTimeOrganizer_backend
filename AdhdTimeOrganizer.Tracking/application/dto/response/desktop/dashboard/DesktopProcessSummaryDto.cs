using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.summaryCards;

namespace AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.desktop.dashboard;

public record DesktopProcessSummaryDto : IDashboardItem
{
    public required string ProcessName { get; set; }
    public string? ProductName { get; set; }

    /// <inheritdoc />
    public string Key => ProcessName;

    /// <inheritdoc />
    public string Label => DashboardItem.LabelOr(ProductName, ProcessName);

    public ActivityStatDto? Active { get; set; }
    public ActivityStatDto? Background { get; set; }
    public bool IsNew { get; set; }
}