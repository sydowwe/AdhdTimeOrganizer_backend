namespace AdhdTimeOrganizer.Core.application.dto.response.activity;

/// <summary>
/// What <c>POST /activity/merge</c> actually did. The success snackbar's two numbers.
/// </summary>
/// <remarks>
/// Returned even though the dialog already predicted both from the <c>usageCount</c>s on screen — that
/// prediction came from a table that may be minutes old, and the merge is irreversible, so the numbers
/// the user is told afterwards should be the ones that happened rather than the ones that were expected.
/// </remarks>
public record MergeActivityResponse
{
    /// <summary>Echo of the request.</summary>
    public required long SurvivorId { get; init; }

    /// <summary>How many activities were deleted. Equals the request's <c>mergedIds</c> length.</summary>
    public required int MergedCount { get; init; }

    /// <summary>
    /// How many rows, across every reference type, now point at the survivor.
    /// </summary>
    /// <remarks>
    /// Rows <em>moved</em>, which is not always rows that survive: where a uniqueness rule made the move
    /// impossible — the survivor already has a backlog profile, or the same draw history — the merged
    /// row is dropped instead and still counted, because from the user's side that reference is equally
    /// resolved. It can therefore come out below the dialog's prediction on a set of activities carrying
    /// overlapping profiles.
    /// </remarks>
    public required int RepointedCount { get; init; }
}
