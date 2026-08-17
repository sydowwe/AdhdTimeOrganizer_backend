namespace AdhdTimeOrganizer.ActivityProfiles.application.dto.request;

/// <summary>
/// What the user has available, and which draw they are looking at.
/// </summary>
public record LeisureSuggestionDrawRequest
{
    /// <summary>Minutes the user has available.</summary>
    public int Minutes { get; set; }

    /// <summary><c>low</c> | <c>medium</c> | <c>high</c>.</summary>
    public string Energy { get; set; } = null!;

    /// <summary>People available, including the user.</summary>
    public int People { get; set; }

    /// <summary>
    /// Highest acceptable <c>ActivityExpectedCostTier</c>, by id; null = any. Resolved through the tiers'
    /// <c>SortOrder</c>, because they are user-editable rows and "no more expensive than this one" is an
    /// ordering question rather than an id comparison.
    /// </summary>
    public long? MaxCostTierId { get; set; }

    /// <summary>Required <c>ActivityLocationType</c>, by id; null = anywhere.</summary>
    public long? LocationTypeId { get; set; }

    /// <summary>
    /// The draw seed, a uint32. <b>Honoured as an input, not decoration.</b> It lives in the page URL, and
    /// the contract the UI depends on is that the same body, against unchanged data and unchanged suggestion
    /// history, returns the same items in the same order — reloading must not reshuffle the cards the user is
    /// deciding between. A different seed is what "something else" sends.
    /// </summary>
    public long Seed { get; set; }

    /// <summary>How many to return. The picker always asks for three.</summary>
    public int Count { get; set; }
}
