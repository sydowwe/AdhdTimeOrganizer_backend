using AdhdTimeOrganizer.Routines.domain.model.@enum;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.Routines.domain.service;

public static class RoutineResetService
{
    // Weekly-aligned: period is <= 7 days OR a multiple of 7 → anchor = day of week (1=Mon…7=Sun)
    // Everything else (10d, 20d, 45d…)                       → anchor = day of month (1–30)
    private static bool IsWeeklyAligned(RoutineTimePeriod period) => period.LengthInDays <= 7 || period.LengthInDays % 7 == 0;

    public static DateTime ComputeNextReset(RoutineTimePeriod period, DateTime now)
    {
        var lastReset = period.LastResetAt ?? period.CreatedTimestamp;
        var earliest = DateTime.SpecifyKind(lastReset.AddDays(period.LengthInDays).Date, DateTimeKind.Utc);

        if (period.ResetAnchorDay == 0)
            return earliest;

        if (IsWeeklyAligned(period))
        {
            var targetDow = period.ResetAnchorDay == 7
                ? DayOfWeek.Sunday
                : (DayOfWeek)period.ResetAnchorDay;

            var daysUntil = ((int)targetDow - (int)earliest.DayOfWeek + 7) % 7;
            return earliest.AddDays(daysUntil);
        }
        else
        {
            // Clamped below via Math.Min against DateTime.DaysInMonth, so any 1–31 value is safe;
            // months shorter than the anchor land on their own last day.
            var targetDay = period.ResetAnchorDay;
            int year, month;

            if (period.LengthInDays == 30)
            {
                // Calendar-month aligned: next reset is targetDay of the next calendar month,
                // advanced further if the routine went dormant for more than one cycle.
                month = lastReset.Month + 1;
                year = lastReset.Year;
                if (month > 12)
                {
                    month = 1;
                    year++;
                }

                var candidate = MonthlyCandidate(year, month, targetDay);
                while (candidate < now)
                {
                    month++;
                    if (month > 12)
                    {
                        month = 1;
                        year++;
                    }
                    candidate = MonthlyCandidate(year, month, targetDay);
                }
                return candidate;
            }

            if (period.LengthInDays == 365)
            {
                // Calendar-year aligned: next reset is targetDay of the same month next year,
                // advanced further if the routine went dormant for more than one cycle.
                month = lastReset.Month;
                year = lastReset.Year + 1;

                var candidate = MonthlyCandidate(year, month, targetDay);
                while (candidate < now)
                {
                    year++;
                    candidate = MonthlyCandidate(year, month, targetDay);
                }
                return candidate;
            }

            // Day-of-month anchor: find next occurrence of that day on or after earliest
            year = earliest.Year;
            month = earliest.Month;

            if (earliest.Day > targetDay)
            {
                month++;
                if (month > 12)
                {
                    month = 1;
                    year++;
                }
            }

            return MonthlyCandidate(year, month, targetDay);
        }
    }

    // Canonical reset instant is midnight UTC, matching the weekly-aligned path above.
    private static DateTime MonthlyCandidate(int year, int month, int targetDay)
    {
        var day = Math.Min(targetDay, DateTime.DaysInMonth(year, month));
        return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
    }

    /// <summary>How many days before <see cref="RoutineTimePeriod.StreakGraceUntil"/> the grace warning fires.</summary>
    public const int GraceWarningLeadDays = 1;

    /// <summary>A lead-time nudge that is due: the reset it is about, and how far off that reset is.</summary>
    public readonly record struct RoutineNudge(DateTime NextReset, int DaysLeft);

    /// <summary>
    /// Whether this period is inside its lead-time window and has not been nudged for this reset yet. Pure —
    /// the caller decides whether there is anything left to nudge <i>about</i> (an all-done period is skipped
    /// without marking, so un-ticking an item tomorrow still earns the nudge) and marks
    /// <see cref="RoutineTimePeriod.EndingSoonNotifiedFor"/> only once a notification actually goes out.
    /// <para>
    /// Returns null once <c>now</c> reaches the reset itself: from that point the reset job owns the period,
    /// and a "3 days left" nudge racing the reset that clears the items would be worse than silence.
    /// </para>
    /// </summary>
    public static RoutineNudge? EvaluateEndingSoon(RoutineTimePeriod period, DateTime now)
    {
        if (period.ReminderLeadDays is not { } leadDays)
            return null;

        var nextReset = ComputeNextReset(period, now);
        if (period.EndingSoonNotifiedFor == nextReset)
            return null;

        if (now < nextReset.AddDays(-leadDays) || now >= nextReset)
            return null;

        // Ceiling, never floor: a reset twelve hours out is "1 day left", not "0 days left".
        return new RoutineNudge(nextReset, (int)Math.Ceiling((nextReset - now).TotalDays));
    }

    /// <summary>
    /// Whether the period's grace window is about to lapse and has not been warned about yet. Keyed on the
    /// grace instant itself, so a later failed period opening a fresh window is a fresh warning.
    /// </summary>
    public static bool ShouldWarnGraceExpiring(RoutineTimePeriod period, DateTime now)
    {
        if (period.StreakGraceUntil is not { } graceUntil || period.GraceNotifiedFor == graceUntil)
            return false;

        return now >= graceUntil.AddDays(-GraceWarningLeadDays) && now < graceUntil;
    }

    /// <summary>
    /// Breaks the streak if the grace period has expired. Should be called before TryReset on every access.
    /// Returns true if the period was modified.
    /// </summary>
    public static bool CheckGrace(RoutineTimePeriod period, DateTime now)
    {
        if (period.StreakGraceUntil == null || now <= period.StreakGraceUntil.Value)
            return false;

        period.Streak = 0;
        period.StreakGraceUntil = null;
        return true;
    }

    /// <summary>
    /// Resets a single item's own checklist state if the period has elapsed, so a stale item looks fresh the
    /// moment it's touched. Deliberately does <b>not</b> advance <see cref="RoutineTimePeriod.LastResetAt"/> or
    /// evaluate the streak — <see cref="ComputeNextReset"/> will keep reporting the same due reset until the
    /// list-based overload runs (background job or the grouped read), which is the only place the streak
    /// transition and <see cref="RoutinePeriodCompletion"/> row are produced. Advancing it here would let this
    /// single-item path silently consume the reset cycle with no streak evaluation.
    /// Use when only one item is in context (e.g. step toggle). Returns true if a reset occurred.
    /// </summary>
    public static bool TryReset(RoutineTimePeriod period, RoutineTodoList item, DateTime now)
    {
        var nextReset = ComputeNextReset(period, now);
        if (now < nextReset)
            return false;

        var today = DateOnly.FromDateTime(now);
        item.SetDone(false);
        item.LastResetDate = today;

        return true;
    }

    /// <summary>
    /// The result of a reset that actually happened: the history row to persist, and what the reset did to the
    /// streak. The outcome is returned rather than re-derived by callers because only this method sees the
    /// streak on both sides of the evaluation.
    /// </summary>
    public readonly record struct RoutinePeriodReset(RoutinePeriodCompletion Completion, StreakOutcome Outcome);

    /// <summary>
    /// Resets items if the period has elapsed. Evaluates period streak before clearing.
    /// Returns the completion record plus the streak outcome if a reset occurred, otherwise null.
    /// </summary>
    public static RoutinePeriodReset? TryReset(RoutineTimePeriod period, IList<RoutineTodoList> items, DateTime now)
    {
        var nextReset = ComputeNextReset(period, now);
        if (now < nextReset)
            return null;

        var completedCount = 0;
        var outcome = StreakOutcome.NotEvaluated;
        if (items.Count > 0)
        {
            completedCount = items.Count(i => i.IsDone);
            var completionPercent = (double)completedCount / items.Count * 100.0;

            if (completionPercent >= period.StreakThreshold)
            {
                period.Streak++;
                if (period.Streak > period.BestStreak)
                    period.BestStreak = period.Streak;
                period.StreakGraceUntil = null;
                outcome = StreakOutcome.Extended;
            }
            else if (period.StreakGraceDays > 0)
            {
                period.StreakGraceUntil = nextReset.AddDays(period.StreakGraceDays);
                outcome = StreakOutcome.OnGrace;
            }
            else
            {
                period.Streak = 0;
                period.StreakGraceUntil = null;
                outcome = StreakOutcome.Broken;
            }
        }

        var today = DateOnly.FromDateTime(now);
        foreach (var item in items)
        {
            item.SetDone(false);
            item.LastResetDate = today;
        }

        var periodEnd = DateOnly.FromDateTime(nextReset);
        period.LastResetAt = nextReset;

        // The window this reset closed is over, so any nudge mark for it is spent — clearing it here is what
        // lets the next cycle earn its own nudge (the mark is compared against a freshly computed instant).
        period.EndingSoonNotifiedFor = null;

        return new RoutinePeriodReset(
            new RoutinePeriodCompletion
            {
                TimePeriodId = period.Id,
                PeriodStart = periodEnd.AddDays(-period.LengthInDays),
                PeriodEnd = periodEnd,
                CompletedCount = completedCount,
                TotalCount = items.Count,
                CreatedAt = now
            },
            outcome);
    }

    /// <summary>
    /// Updates the item's streak after a toggle. Call after IsDoneLogic has been applied.
    /// </summary>
    public static void UpdateItemStreak(RoutineTodoList item, DateTime now)
    {
        if (!item.IsDone)
            return;

        item.LastCompletedAt = now;
        item.Streak++;
        if (item.Streak > item.BestStreak)
            item.BestStreak = item.Streak;
    }
}