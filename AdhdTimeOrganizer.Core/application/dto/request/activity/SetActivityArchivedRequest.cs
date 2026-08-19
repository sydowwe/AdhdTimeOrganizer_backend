namespace AdhdTimeOrganizer.Core.application.dto.request.activity;

/// <summary>
/// Body of <c>PATCH /activity/{id}/archived</c>. One endpoint for both directions rather than
/// <c>/archive</c> + <c>/unarchive</c>, so there is one rule and one handler.
/// </summary>
public record SetActivityArchivedRequest
{
    /// <summary>
    /// Target state, not a toggle. A toggle would race the row action against a stale table — two clicks
    /// on a table loaded a minute ago would land wherever the server happened to be — and it is the state
    /// the UI already knows it wants.
    /// </summary>
    public required bool IsArchived { get; init; }
}
