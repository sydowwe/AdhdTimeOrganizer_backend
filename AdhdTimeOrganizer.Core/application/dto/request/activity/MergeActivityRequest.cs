namespace AdhdTimeOrganizer.Core.application.dto.request.activity;

/// <summary>
/// Body of <c>POST /activity/merge</c>: fold <see cref="MergedIds"/> into <see cref="SurvivorId"/>.
/// </summary>
public record MergeActivityRequest
{
    /// <summary>The activity that stays. Its name, role, category and text are the merged result.</summary>
    public required long SurvivorId { get; init; }

    /// <summary>
    /// The activities to fold in and delete. Never empty and never contains <see cref="SurvivorId"/> —
    /// the dialog strips it — so both are 400s rather than tolerated inputs: reaching either means the
    /// client is broken, not that the user is in an odd state.
    /// </summary>
    public required List<long> MergedIds { get; init; } = [];
}
