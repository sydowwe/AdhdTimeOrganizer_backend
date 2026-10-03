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
}
