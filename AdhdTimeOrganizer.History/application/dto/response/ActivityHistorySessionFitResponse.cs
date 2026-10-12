namespace AdhdTimeOrganizer.History.application.dto.response.activityHistory;

/// <summary>
/// The answer to <c>GET /activity-history/session/fit</c> (F2-07): a focus length that usually works for the
/// activity, or nothing.
/// </summary>
/// <remarks>
/// Deliberately carries no completion share and no count of stopped sessions. The client offers a length, and a
/// figure like "stopped early 60% of the time" would read as a score of the person.
/// </remarks>
public record ActivityHistorySessionFitResponse
{
    /// <summary>
    /// A whole number of five-minute steps, reached by most focus stretches on this activity. <c>null</c> when
    /// there are too few sessions to say. Never written into a timer preset.
    /// </summary>
    public long? SuggestedSeconds { get; init; }

    /// <summary>The focus stretches that bore on <see cref="SuggestedSeconds"/>; <c>null</c> whenever that is.</summary>
    public int? SampleCount { get; init; }
}
