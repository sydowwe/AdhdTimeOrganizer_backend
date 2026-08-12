using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.summaryCards;

namespace AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.desktop.dashboard;

public record DesktopProcessSummaryDto
{
    public required string ProcessName { get; set; }
    public string? ProductName { get; set; }
    public ActivityStatDto? Active { get; set; }
    public ActivityStatDto? Background { get; set; }
    public bool IsNew { get; set; }
}