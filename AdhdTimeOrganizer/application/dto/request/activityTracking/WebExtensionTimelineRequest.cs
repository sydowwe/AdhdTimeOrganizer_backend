using FastEndpoints;

namespace AdhdTimeOrganizer.application.dto.request.activityTracking;

public record WebExtensionTimelineRequest
{
    [QueryParam]
    public DateTime From { get; set; }

    [QueryParam]
    public DateTime To { get; set; }

    [QueryParam]
    public int? MinSeconds { get; set; }  // Filter out sessions shorter than this
}
