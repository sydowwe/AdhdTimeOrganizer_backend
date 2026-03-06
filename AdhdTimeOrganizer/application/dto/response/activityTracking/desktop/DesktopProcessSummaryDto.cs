using AdhdTimeOrganizer.application.dto.response.activityTracking.summaryCards;

namespace AdhdTimeOrganizer.application.dto.response.activityTracking.desktop;

public record DesktopProcessSummaryDto
{
    public string ProcessName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public ActivityStatDto? Active { get; set; }
    public ActivityStatDto? Background { get; set; }
    public bool IsNew { get; set; }
}
