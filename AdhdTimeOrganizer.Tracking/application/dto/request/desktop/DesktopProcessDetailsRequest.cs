using FastEndpoints;

namespace AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.desktop;

public record DesktopProcessDetailsRequest : DailyWindowMaskRequest
{
    [QueryParam]
    public string ProcessName { get; set; } = string.Empty;
}
