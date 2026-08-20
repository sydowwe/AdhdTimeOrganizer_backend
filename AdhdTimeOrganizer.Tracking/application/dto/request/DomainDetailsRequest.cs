using FastEndpoints;

namespace AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;

public record DomainDetailsRequest : DailyWindowMaskRequest
{
    [QueryParam]
    public string Domain { get; set; } = string.Empty;
}
