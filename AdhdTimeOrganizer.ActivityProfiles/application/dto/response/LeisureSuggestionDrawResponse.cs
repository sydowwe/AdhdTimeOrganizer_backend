namespace AdhdTimeOrganizer.ActivityProfiles.application.dto.response;

/// <summary>
/// One card of a draw. Facts only — there is no score, rank or explanation field on purpose: the card
/// explains itself from these, and a scoring number would be an unrenderable detail the frontend would have
/// to be re-released to use.
/// </summary>
public record LeisureSuggestionItemResponse
{
    /// <summary>
    /// <c>"&lt;source&gt;:&lt;activityId&gt;"</c>. Identity across draws — what the UI keys cards on and what
    /// <c>POST /leisure-suggestion/seen</c> sends back.
    /// </summary>
    public required string Key { get; init; }

    /// <summary><c>backlog</c> | <c>project</c> | <c>bucketList</c>.</summary>
    public required string Source { get; init; }

    public required long ActivityId { get; init; }
    public required ActivityInfoResponse Activity { get; init; }

    /// <summary>
    /// The duration the source actually records; backlog only. Null everywhere else, which is the difference
    /// between a card that says "Takes 45 min" and one that says "Fits in 45 min".
    /// </summary>
    public int? StatedDurationMinutes { get; init; }

    /// <summary>
    /// The longest this could usefully occupy — the backlog's duration, or a project's whole estimate in
    /// minutes. Null when the source records neither. Used only to size the planner slot the user books.
    /// </summary>
    public int? MaxUsefulMinutes { get; init; }

    /// <summary><c>low</c> | <c>medium</c> | <c>high</c>.</summary>
    public required string EnergyLevel { get; init; }

    /// <summary>
    /// False when the source states the energy (backlog), true when it was inferred from difficulty or
    /// comfort-zone step. The card labels a derived value as an estimate, so this has to be honest.
    /// </summary>
    public required bool EnergyIsDerived { get; init; }

    /// <summary><c>physical</c> | <c>mental</c>, or null.</summary>
    public string? EffortType { get; init; }

    /// <summary>Backlog only; null elsewhere.</summary>
    public int? MinParticipants { get; init; }

    /// <summary>Project only: <c>planning</c> | <c>readyToStart</c>. <c>needsShopping</c> never appears in a draw.</summary>
    public string? ReadinessStatus { get; init; }

    /// <summary>Bucket list only, 1–5.</summary>
    public int? ComfortZoneStep { get; init; }

    /// <summary>Bucket list only; false elsewhere.</summary>
    public required bool RequiresTravel { get; init; }

    /// <summary>
    /// The one source-specific fact worth a line on the card: the backlog's location-type text, the
    /// bucket-list entry's experience-type text, the project's area. Already resolved to display text — the
    /// client does not look the lookup row up.
    /// </summary>
    public string? ContextLabel { get; init; }
}

/// <summary>
/// A ranked draw, plus the two counts the empty state is built from.
/// </summary>
public record LeisureSuggestionDrawResponse
{
    /// <summary>The draw, at most <c>count</c>, best first. May be shorter, including empty.</summary>
    public required List<LeisureSuggestionItemResponse> Items { get; init; }

    /// <summary>
    /// Candidates considered before any constraint was applied — every backlog, project and bucket-list
    /// profile the user has filed.
    /// </summary>
    public required int PoolCount { get; init; }

    /// <summary>Candidates that survived the constraints.</summary>
    public required int EligibleCount { get; init; }

    // These two are not statistics, they are what the empty state says: PoolCount == 0 renders "you have
    // nothing filed yet, go add some", while PoolCount > 0 with no items renders "nothing matches, try
    // loosening a constraint". Confusing them is the difference between blaming the user and blaming the
    // filter — which is why PoolCount counts rows before *every* constraint, including the source floors that
    // keep a whole table out of a 30-minute draw.
}
