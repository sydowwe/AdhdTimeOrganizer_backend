namespace AdhdTimeOrganizer.application.dto.response.user;

/// <summary>
/// <c>GET /welcome-back</c> (FO5): what piled up while the user was gone, for the one-time card on home. Absent
/// (204) when there is no card to show: the user was not away for <c>UserPresence.GreetAfter</c>, already answered
/// it, dismissed it, or came back more than <c>UserPresence.CardLifetime</c> ago.
/// <para>
/// <b>The counts are for deciding what to render, not for leading with.</b> The card is never led by numbers: a row
/// with nothing in it is left out, and a row with something in it names a few items before it says how many.
/// </para>
/// </summary>
public record WelcomeBackResponse
{
    /// <summary>When the user was last in the app before the absence (UTC).</summary>
    public required DateTime AwayFrom { get; init; }

    /// <summary>When they came back (UTC).</summary>
    public required DateTime ReturnedAt { get; init; }

    /// <summary>Calendar days between the two, on the user's own clock. At least 4.</summary>
    public required int DaysAway { get; init; }

    /// <summary>
    /// At least half of the days in between were marked <c>Vacation</c> or <c>SickDay</c> (FO1's rule). The absence
    /// was announced, so the copy can say "welcome back from your break" rather than "it's been a while".
    /// </summary>
    public required bool WasMarkedAway { get; init; }

    /// <summary>Open to-do items whose due date is before the user's today, oldest first.</summary>
    public required WelcomeBackPileResponse OverdueTodos { get; init; }

    /// <summary>
    /// Planner blocks left <c>NotStarted</c> or <c>InProgress</c> (not background) from the day the absence began up
    /// to yesterday, oldest first. This deliberately overlaps the day view's seven-day carry-over banner: answering
    /// the card clears both.
    /// </summary>
    public required WelcomeBackPileResponse PastTasks { get; init; }

    /// <summary>
    /// Routines whose streak ended while the user was gone: a cycle closing in the absence missed its threshold,
    /// was neither frozen nor excused, and the streak now stands at 0. Nothing to decide on the card itself; it says
    /// they start fresh today, and points to the skip for each one a skip can still bring back (<c>CanFreeze</c>).
    /// </summary>
    public required IReadOnlyList<WelcomeBackRoutineResponse> BrokenRoutines { get; init; }

    /// <summary>Unread notifications, all of them (not only those raised during the absence).</summary>
    public required int UnreadNotifications { get; init; }
}

/// <summary>One row of the card: how many, and the first few by name.</summary>
public record WelcomeBackPileResponse
{
    public required int Count { get; init; }

    /// <summary>At most <c>WelcomeBackPile.PreviewSize</c>, in the pile's own order.</summary>
    public required IReadOnlyList<WelcomeBackItemResponse> Preview { get; init; }
}

/// <param name="Id">The to-do item's or planner task's id.</param>
/// <param name="Name">The activity's name — what both kinds of row are titled by.</param>
/// <param name="Date">The item's due date, or the task's day.</param>
public record WelcomeBackItemResponse(long Id, string Name, DateOnly Date);

/// <param name="Id">The routine period's id (<c>RoutineTimePeriod</c>).</param>
/// <param name="Text">Its name, e.g. "Daily".</param>
/// <param name="Color">Its colour, as the routines screen draws it.</param>
/// <param name="CanFreeze">
/// Whether spending a skip (a streak freeze) on its newest cycle would succeed now and bring the run back —
/// <c>RoutineStreakFreezeService.DescribeBreak</c>, the rule the freeze endpoint and the broken-streak notification
/// share (F1-04: "whether a freeze can still cover one").
/// </param>
public record WelcomeBackRoutineResponse(long Id, string Text, string Color, bool CanFreeze);
