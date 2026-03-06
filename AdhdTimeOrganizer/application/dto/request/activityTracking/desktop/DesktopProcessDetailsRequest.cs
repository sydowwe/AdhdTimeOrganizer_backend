using FastEndpoints;

namespace AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;

public record DesktopProcessDetailsRequest
{
    [QueryParam]
    public DateTime From { get; set; }

    [QueryParam]
    public DateTime To { get; set; }

    [QueryParam]
    public string ProcessName { get; set; } = string.Empty;
}
