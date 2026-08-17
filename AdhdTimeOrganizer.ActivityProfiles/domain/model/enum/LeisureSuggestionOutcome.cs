namespace AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;

/// <summary>
/// What became of a suggestion the user was shown.
/// <para>
/// Both outcomes bury the candidate for a while — that is what stops the same three appearing every
/// visit — so staleness reads <c>LastSuggestedAt</c> without caring which one it was. The distinction
/// exists for the effort-variety signal: only a <see cref="Committed"/> row says what the user
/// actually chose to do, and the next draw varies away from that.
/// </para>
/// <para>
/// A merely *rendered* draw is deliberately not an outcome. Recording on render would demote the very
/// cards on screen when the page reloaded, and a seeded URL would stop reproducing its own draw.
/// </para>
/// </summary>
public enum LeisureSuggestionOutcome
{
    Rejected,
    Committed
}
