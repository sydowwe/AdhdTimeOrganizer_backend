namespace AdhdTimeOrganizer.application.dto.request.user;

/// <summary>
/// <c>POST /welcome-back/fresh-start</c> (FO5): the card's three choices, applied in one call. Each defaults to
/// leaving that pile alone, so a client that only renders some of the rows sends only those.
/// </summary>
public record ApplyFreshStartRequest
{
    public FreshStartTodoChoice Todos { get; init; } = FreshStartTodoChoice.Keep;

    /// <summary>
    /// With <see cref="FreshStartTodoChoice.Spread"/>: how many days, today included, the overdue to-dos are spread
    /// over (1–14). Ignored otherwise.
    /// </summary>
    public int SpreadOverDays { get; init; }

    public FreshStartTaskChoice PastTasks { get; init; } = FreshStartTaskChoice.Keep;

    /// <summary>Mark every unread notification read.</summary>
    public bool MarkNotificationsRead { get; init; }
}

/// <summary>What happens to the overdue to-dos. On the wire as the member name.</summary>
public enum FreshStartTodoChoice
{
    Keep,

    /// <summary>The due date is cleared: the item stays on its list with no deadline. Not counted as a postponement.</summary>
    Someday,

    /// <summary>
    /// Oldest first, round-robin over today and the next <c>SpreadOverDays − 1</c> days, so no one day gets the
    /// whole pile. Each move is a postponement under FO3's rule, exactly as the overdue banner's "move them all" is.
    /// </summary>
    Spread
}

/// <summary>What happens to the planner blocks left open while the user was away. On the wire as the member name.</summary>
public enum FreshStartTaskChoice
{
    Keep,

    /// <summary>Skipped (<c>Cancelled</c>) with the server-only reason <c>away</c>, which counts as no failed attempt.</summary>
    CloseAsAway,

    /// <summary>
    /// Carried to today at the same time of day, with FO3's carry-over record (<c>OriginalDate</c> set once,
    /// <c>CarryOverCount</c> + 1). A repeating rule's occurrence is closed as away instead: the rule places today's own.
    /// </summary>
    CarryToToday
}
