using FastEndpoints;

namespace AdhdTimeOrganizer.application.dto.request.activityTracking;

public record DomainDetailsRequest
{
    [QueryParam]
    public DateTime From { get; set; }

    [QueryParam]
    public DateTime To { get; set; }

    [QueryParam]
    public string Domain { get; set; } = string.Empty;
}
