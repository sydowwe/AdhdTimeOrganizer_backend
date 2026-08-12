namespace AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;

/// <summary>
/// The day-plan completion streak, as the client should display it. Rides along on
/// <see cref="CalendarResponse"/> rather than being its own route: the home page already fires four to six
/// requests on mount, and this is needed on every one of those loads.
/// <para>
/// <b><see cref="CurrentStreak"/> is the value to render, unconditionally.</b> There is no "is it still
/// alive" decision left for the client — a dead streak is already 0 here. That judgement depends on the skip
/// rule, the empty-day rule and (were there one) the grace rule, all of which live server-side in
/// <c>PlannerStreakService</c>; a client that re-derives it from a last-completed date is guessing, which is
/// exactly how the localStorage store this replaces went wrong.
/// </para>
/// </summary>
public record PlannerStreakResponse
{
    /// <summary>Days completed in the run still open today. Already zeroed when the streak has broken.</summary>
    public required int CurrentStreak { get; init; }

    /// <summary>The longest run ever held, over the user's whole planning history. Never below <see cref="CurrentStreak"/>.</summary>
    public required int BestStreak { get; init; }

    /// <summary>
    /// Whether today's plan is complete under the streak's own rule — which is <i>not</i> the progress ring's
    /// rule. Optional and background tasks are excluded here and skipped tasks leave the denominator, so a day
    /// showing 4/5 on the ring can still be complete. Read this rather than comparing counts.
    /// </summary>
    public required bool IsTodayComplete { get; init; }

    /// <summary>
    /// The date the server treated as "today", and <see cref="Timezone"/> is the zone it used to get there —
    /// the user's own <c>User.Timezone</c>, not UTC and not the browser clock. Returned because the B1 ask
    /// asked the server to decide the day boundary and then say which one it used; the client should stop
    /// computing dates for this and can assert against these two if it ever disagrees.
    /// </summary>
    public required DateOnly Today { get; init; }

    /// <inheritdoc cref="Today"/>
    public required string Timezone { get; init; }
}
