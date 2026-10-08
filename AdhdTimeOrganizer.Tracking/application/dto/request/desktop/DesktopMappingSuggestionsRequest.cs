using FastEndpoints;

namespace AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.desktop;

public record DesktopMappingSuggestionsRequest
{
    /// <summary>How many days back to learn from, ending now. Defaults to 30.</summary>
    [QueryParam]
    public int Days { get; set; } = 30;
}
