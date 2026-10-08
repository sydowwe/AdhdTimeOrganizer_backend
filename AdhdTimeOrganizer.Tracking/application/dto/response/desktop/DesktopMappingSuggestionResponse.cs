namespace AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.desktop;

/// <summary>
/// "Suggested: <see cref="ActivityName"/> — seen in <see cref="SessionCount"/> of your
/// <see cref="ActivitySessionCount"/> timers": an uncovered process that the user's own timer focus phases point at
/// one activity (FO9, F3-02). A suggestion only — accepting it is the ordinary rule create, from the client.
/// </summary>
public record DesktopMappingSuggestionResponse
{
    /// <summary>The process the suggested rule would match, exactly.</summary>
    public required string ProcessName { get; init; }

    /// <summary>Its busiest product name, for the label.</summary>
    public required string ProductName { get; init; }

    public required long ActivityId { get; init; }

    public required string ActivityName { get; init; }

    /// <summary>Timer sittings on the activity in which the process was active.</summary>
    public required int SessionCount { get; init; }

    /// <summary>Every timer sitting on the activity in the window, whether or not the process appeared.</summary>
    public required int ActivitySessionCount { get; init; }

    /// <summary>Active seconds of the process inside those sittings.</summary>
    public required int Seconds { get; init; }
}
