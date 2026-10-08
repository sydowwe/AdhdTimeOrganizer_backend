using FastEndpoints;

namespace AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;

/// <summary>The android twin of <see cref="desktop.DesktopMappingSuggestionsRequest"/>.</summary>
public record AndroidMappingSuggestionsRequest
{
    /// <summary>How many days back to learn from, ending now. Defaults to 30.</summary>
    [QueryParam]
    public int Days { get; set; } = 30;
}
