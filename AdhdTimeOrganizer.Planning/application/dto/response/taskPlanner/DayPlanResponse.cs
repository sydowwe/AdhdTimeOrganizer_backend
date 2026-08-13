namespace AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;

/// <summary>
/// One day's plan in one response — the calendar row, its planner tasks and the user's streak.
/// <para>
/// Exists because the home page could not ask for a day's tasks without first learning the day's calendar id:
/// <c>PlannerTaskFilter</c> is keyed on <c>CalendarId</c>, and the only source of that id was
/// <c>GET /calendar/by-Date/{date}</c>. The two hops were serialised <i>by contract</i>, not by fan-out, and
/// after the dashboard-refresh work they are paid again on every stale tab-return and every five-minute
/// backstop poll rather than once per navigation.
/// </para>
/// <para>
/// <b>Absent is not an error.</b> A date with no calendar row comes back 200 with <see cref="Calendar"/> null
/// and <see cref="Tasks"/> empty, never 404 — the client's "no plan yet" state has to be reachable through a
/// successful response, because a rejected promise there renders a retry button over a day that is simply
/// unplanned. The sibling <c>by-Date</c> route keeps its 404; it is a lookup, this is a page load.
/// </para>
/// </summary>
public record DayPlanResponse
{
    /// <summary>The date this plan is for, echoed back as parsed. Always set, calendar row or not.</summary>
    public required DateOnly Date { get; init; }

    /// <summary>
    /// The day's calendar row, or null when the user has none for this date. Carries the same shape as
    /// <c>GET /calendar/by-Date</c> minus the streak, which is hoisted to <see cref="Streak"/> here so it
    /// survives a null calendar.
    /// </summary>
    public required CalendarResponse? Calendar { get; init; }

    /// <summary>
    /// Every planner task on that calendar, ordered by start time. The whole day — no time window, because
    /// <c>PlannerTaskFilter</c>'s <c>From</c>/<c>Until</c> only ever carried 00:00–23:59 from this caller.
    /// Empty when there is no calendar; a task cannot exist without one (<c>PlannerTask.CalendarId</c> is
    /// non-nullable).
    /// </summary>
    public required IReadOnlyList<PlannerTaskResponse> Tasks { get; init; }

    /// <summary>
    /// The user's day-plan completion streak, always present. Hoisted out of <see cref="CalendarResponse"/>
    /// deliberately: it is a fact about the <i>user</i>, so it is still true — and still displayable — on a
    /// day that has no calendar row at all.
    /// </summary>
    public required PlannerStreakResponse Streak { get; init; }

    /// <summary>
    /// Whether this day has actually been planned, which is <b>not</b> the same question as whether
    /// <see cref="Calendar"/> is null.
    /// <para>
    /// Calendar rows are bulk-seeded per user for whole years by <c>CalendarSeeder</c> — they are not created
    /// lazily on first task. So inside the seeded horizon every date has a row whether or not the user ever
    /// planned it, and outside it no date does, no matter how much the user plans. A client that branches on
    /// calendar presence is branching on "is this date inside the seeded years", which is never the question
    /// it means to ask. Branch on this instead.
    /// </para>
    /// </summary>
    public bool HasPlan => Tasks.Count > 0;
}
