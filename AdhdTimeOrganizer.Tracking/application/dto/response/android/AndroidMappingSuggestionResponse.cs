namespace AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.android;

/// <summary>
/// The android twin of <see cref="desktop.DesktopMappingSuggestionResponse"/>: an uncovered app that the user's own
/// timer focus phases point at one activity, and that filled those timers (FO9). A suggestion only — accepting it is
/// the ordinary rule create, from the client.
/// </summary>
public record AndroidMappingSuggestionResponse
{
    /// <summary>The package the suggested rule would match, exactly.</summary>
    public required string PackageName { get; init; }

    /// <summary>Its busiest app label, for the label.</summary>
    public required string AppLabel { get; init; }

    public required long ActivityId { get; init; }

    public required string ActivityName { get; init; }

    /// <summary>Timer sittings on the activity in which the app was in the foreground.</summary>
    public required int SessionCount { get; init; }

    /// <summary>Every timer sitting on the activity in the window, whether or not the app appeared.</summary>
    public required int ActivitySessionCount { get; init; }

    /// <summary>Foreground seconds of the app inside those sittings.</summary>
    public required int Seconds { get; init; }
}
