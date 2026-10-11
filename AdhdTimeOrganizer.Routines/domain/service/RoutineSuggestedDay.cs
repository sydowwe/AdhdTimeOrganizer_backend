using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.Routines.domain.service;

/// <summary>
/// Which day a routine item is meant for. The server half of a rule the client also ships
/// (<c>src/core/todoList/dto/response/routine/routineSuggestedDay.ts</c>) — the two must agree, or the digest
/// names an item the list does not mark as today's, or the other way round.
/// </summary>
public static class RoutineSuggestedDay
{
    /// <summary>
    /// Whether <paramref name="item"/> is suggested for <paramref name="date"/>, the user's own local date.
    /// <list type="bullet">
    /// <item>Daily periods (<c>LengthInDays == 1</c>) never are. They are suggested every day by definition, so
    /// the answer is decided by the period's length, not the fields — an item may carry all seven weekdays
    /// anyway (the e2e seed's do, though the create validator would refuse them).</item>
    /// <item>Weekly (<c>≤ 14</c>): <see cref="RoutineTodoList.SuggestedDays"/> contains the date's weekday.</item>
    /// <item>Monthly and longer (<c>&gt; 14</c>): <see cref="RoutineTodoList.SuggestedDayOfMonth"/> equals the day of
    /// the month, clamped to the month's last day — "the 31st" is 30 September, "the 30th" is 28 February. A
    /// month is never skipped, the same clamp <c>RoutineResetService</c> applies to reset anchors.</item>
    /// </list>
    /// An item with nothing set is suggested on no day. The field that does not belong to the period's length
    /// is ignored rather than trusted — <c>RoutineTodoListPeriodRules</c> refuses it on write, but a period's
    /// length can be edited after its items were saved.
    /// </summary>
    public static bool IsSuggestedOn(RoutineTimePeriod period, RoutineTodoList item, DateOnly date)
    {
        if (period.LengthInDays <= 1)
            return false;

        if (period.LengthInDays <= 14)
            return item.SuggestedDays.Contains(date.DayOfWeek);

        return item.SuggestedDayOfMonth is { } day
               && Math.Min(day, DateTime.DaysInMonth(date.Year, date.Month)) == date.Day;
    }

    /// <summary>
    /// What a morning message counts as "suggested for today": a visible period's item suggested on
    /// <paramref name="date"/> (<see cref="IsSuggestedOn"/>) and not <see cref="IsDoneThisCycle">done this cycle</see>.
    /// One rule for the routine slice's own digest and the host's morning briefing, so the two cannot disagree about
    /// which items the user still has to do today.
    /// </summary>
    public static bool IsOpenSuggestionOn(RoutineTimePeriod period, RoutineTodoList item, DateOnly date, DateTime utcNow) =>
        !period.IsHidden && IsSuggestedOn(period, item, date) && !IsDoneThisCycle(period, item, utcNow);

    /// <summary>
    /// Done <i>in the cycle that is still open</i>. Resets fall on midnight UTC but are only <i>applied</i> by the
    /// 02:00 UTC job or lazily on the next read, so at a user's morning the reset that closes last cycle may be
    /// due and not yet applied — an item ticked last Tuesday still reads <c>IsDone</c>. Past the due reset, that
    /// tick belongs to a closed cycle and says nothing about today. Before it, the tick is the current cycle's by
    /// the server's own definition, which is also what the routine list shows.
    /// </summary>
    public static bool IsDoneThisCycle(RoutineTimePeriod period, RoutineTodoList item, DateTime utcNow) =>
        item.IsDone && utcNow < RoutineResetService.ComputeNextReset(period, utcNow);
}
