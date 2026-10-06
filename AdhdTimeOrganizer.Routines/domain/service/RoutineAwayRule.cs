using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.Routines.domain.service;

/// <summary>
/// When a missed cycle does not count because the user was away (vacation or sick — Core's
/// <c>DayTypeExtensions.IsAway</c>). Static and pure like the rest of the streak logic: callers pre-read the
/// away dates through <c>ICalendarDayLookup.GetAwayDatesAsync</c> and hand them in as a set.
/// <para>
/// <b>The rule</b> (owner decision, 2026-10-06): a cycle is excused when <b>at least half</b> of its days are
/// away days. For a daily routine that is the day itself; for a weekly one, four of its seven days — so a
/// three-day flu inside a week is not excused, and the user still has the other four days to do it.
/// </para>
/// <para>
/// <b>Time zones.</b> A cycle's days are the UTC dates of its boundaries (<see cref="RoutinePeriodCompletion"/>'s
/// <c>PeriodStart</c> / <c>PeriodEnd</c>, midnight UTC), while away days are the user's local calendar dates.
/// For a zone far from UTC the two can disagree by one day at a cycle's edges. The majority rule absorbs that:
/// one edge day shifts the count by at most one, and a cycle that hinges on that day was borderline anyway.
/// </para>
/// </summary>
public static class RoutineAwayRule
{
    /// <summary>
    /// Whether the cycle <c>[periodStart, periodEnd)</c> — end exclusive, because <c>periodEnd</c> is the
    /// boundary that opened the next cycle — is mostly away.
    /// </summary>
    public static bool IsMostlyAway(IReadOnlySet<DateOnly>? awayDays, DateOnly periodStart, DateOnly periodEnd)
    {
        if (awayDays is not { Count: > 0 })
            return false;

        var length = periodEnd.DayNumber - periodStart.DayNumber;
        if (length <= 0)
            return false;

        var away = 0;
        for (var day = periodStart; day < periodEnd; day = day.AddDays(1))
        {
            if (awayDays.Contains(day))
                away++;
        }

        return away * 2 >= length;
    }

    /// <summary>
    /// Whether a recorded cycle may be excused: it must be a real miss — not already frozen or excused, not an
    /// empty cycle (which carries the run for free), not one the user actually met — and mostly away.
    /// Excusal never turns a met cycle into anything else.
    /// </summary>
    public static bool CanExcuse(
        RoutinePeriodCompletion completion, int streakThreshold, IReadOnlySet<DateOnly>? awayDays) =>
        !completion.IsFrozen
        && !completion.IsExcused
        && completion.TotalCount > 0
        && !RoutineStreakFreezeService.IsSuccess(completion, streakThreshold)
        && IsMostlyAway(awayDays, completion.PeriodStart, completion.PeriodEnd);
}
