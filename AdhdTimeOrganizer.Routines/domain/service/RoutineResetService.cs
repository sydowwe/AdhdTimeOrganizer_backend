using AdhdTimeOrganizer.Routines.domain.model.@enum;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.Routines.domain.service;

public static class RoutineResetService
{
    // Weekly-aligned: period is <= 7 days OR a multiple of 7 → anchor = day of week (1=Mon…7=Sun)
    // Everything else (10d, 20d, 45d…)                       → anchor = day of month (1–30)
    private static bool IsWeeklyAligned(RoutineTimePeriod period) => period.LengthInDays <= 7 || period.LengthInDays % 7 == 0;

    /// <summary>
    /// The boundary that closes the cycle <see cref="RoutineTimePeriod.LastResetAt"/> opened. Derived from
    /// <c>LastResetAt</c> alone — <paramref name="now"/> is accepted for symmetry with the rest of the service
    /// and deliberately does not move the answer.
    /// <para>
    /// The returned instant is therefore <b>allowed to be in the past</b>: being in the past is exactly what
    /// makes a period due, which is what every caller's <c>now &lt; nextReset</c> gate reads. A period that has
    /// gone dormant for several cycles reports its oldest unclosed boundary here; catching the rest up is
    /// <see cref="TryResetCatchUp"/>'s job.
    /// </para>
    /// </summary>
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
                // Calendar-month aligned: next reset is targetDay of the calendar month after the one the last
                // reset landed in. Anchored on lastReset, never on `now` — advancing to a boundary in the future
                // would make these periods permanently un-resettable, since the boundary is midnight UTC and no
                // caller ever observes the period at midnight to the tick. Dormancy is caught up one boundary at
                // a time by TryResetCatchUp, exactly like the weekly-aligned path above.
                month = lastReset.Month + 1;
                year = lastReset.Year;
                if (month > 12)
                {
                    month = 1;
                    year++;
                }

                return MonthlyCandidate(year, month, targetDay);
            }

            if (period.LengthInDays == 365)
            {
                // Calendar-year aligned: targetDay of the same month next year. Anchored on lastReset for the
                // same reason as the monthly branch above.
                month = lastReset.Month;
                year = lastReset.Year + 1;

                return MonthlyCandidate(year, month, targetDay);
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
    /// <para>
    /// Closes <b>one</b> cycle: <c>LastResetAt</c> lands on the boundary that was due, which may still be in the
    /// past. Callers reaching a period that has been dormant want <see cref="TryResetCatchUp"/>, which loops
    /// this until the period is current.
    /// </para>
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
    /// Hard stop on the catch-up loop, so a period with an absurd <see cref="RoutineTimePeriod.LastResetAt"/>
    /// (a clock skew, a hand-edited row) cannot turn one request into an unbounded walk. A daily period needs
    /// one iteration per dormant day, so this is roughly three years of dormancy in a single pass; anything
    /// beyond it simply continues on the next access.
    /// </summary>
    private const int MaxCatchUpCycles = 1_200;

    /// <summary>
    /// Every cycle a catch-up closed: the completion rows worth persisting (the newest
    /// <see cref="RoutineTimePeriod.HistoryDepth"/> of them — older ones would fall straight off the heatmap),
    /// the last cycle's result, and how many cycles were closed in total.
    /// <para>
    /// <see cref="Latest"/> is what the caller notifies on: a period dormant for two months owes the user one
    /// "your period ended" summary, not sixty. <see cref="CyclesClosed"/> is there so a caller can say so.
    /// </para>
    /// </summary>
    public readonly record struct RoutinePeriodCatchUp(
        IReadOnlyList<RoutinePeriodCompletion> Completions,
        RoutinePeriodReset Latest,
        int CyclesClosed);

    /// <summary>
    /// Runs <see cref="TryReset(RoutineTimePeriod, IList{RoutineTodoList}, DateTime)"/> until the period is
    /// current, rather than once per invocation. Returns null if nothing was due.
    /// <para>
    /// A single <c>TryReset</c> advances <c>LastResetAt</c> by exactly one boundary, so a period untouched for
    /// sixty cycles would otherwise need sixty invocations — one per nightly run — to become current, each one
    /// notifying about a miss weeks old. This closes all of them in one pass and hands the caller a single
    /// notification.
    /// </para>
    /// <para>
    /// The streak is evaluated for every closed cycle (they are all misses, since the items sat untouched), and
    /// a grace window opened by one cycle is checked against the <i>next</i> boundary rather than against
    /// <paramref name="now"/> — so a catch-up breaks the streak at the same cycle a live run would have.
    /// </para>
    /// </summary>
    public static RoutinePeriodCatchUp? TryResetCatchUp(RoutineTimePeriod period, IList<RoutineTodoList> items, DateTime now)
    {
        var completions = new List<RoutinePeriodCompletion>();
        RoutinePeriodReset? latest = null;
        var cyclesClosed = 0;

        while (cyclesClosed < MaxCatchUpCycles)
        {
            var boundary = ComputeNextReset(period, now);
            if (now < boundary)
                break;

            // Not `now`: the window belongs to the cycle that opened it, and it lapses at its own instant.
            if (cyclesClosed > 0)
                CheckGrace(period, boundary);

            if (TryReset(period, items, now) is not { } result)
                break;

            completions.Add(result.Completion);
            latest = result;
            cyclesClosed++;
        }

        if (latest is not { } last)
            return null;

        // Only the newest HistoryDepth rows are ever read back, so writing the rest is pure churn.
        if (completions.Count > period.HistoryDepth)
            completions.RemoveRange(0, completions.Count - period.HistoryDepth);

        return new RoutinePeriodCatchUp(completions, last, cyclesClosed);
    }

    /// <summary>
    /// Whether this item has already been counted towards its streak in the cycle it is currently in.
    /// <para>
    /// The cycle opened at <see cref="RoutineTodoList.LastResetDate"/> — every reset path stamps it — so a
    /// completion at or after that day belongs to the cycle in progress. A null <c>LastResetDate</c> means the
    /// item has never been reset, so its first cycle runs from creation and any completion at all counts.
    /// </para>
    /// <para>
    /// Deliberately compared on the item alone rather than against <see cref="RoutineTimePeriod.LastResetAt"/>:
    /// the two event handlers in the host that complete a routine item load it without its period, and a guard
    /// they cannot call is a guard with two holes in it. The cost is day granularity — a cycle that is both reset
    /// and completed twice within one calendar day scores the second completion as already-counted — which needs
    /// the single-item <see cref="TryReset(RoutineTimePeriod, RoutineTodoList, DateTime)"/> and a list reset to
    /// land on the same day to reach at all.
    /// </para>
    /// </summary>
    private static bool AlreadyCountedThisCycle(RoutineTodoList item)
    {
        if (item.LastCompletedAt == default)
            return false;

        return item.LastResetDate is not { } lastReset || DateOnly.FromDateTime(item.LastCompletedAt) >= lastReset;
    }

    /// <summary>
    /// Updates the item's streak after a toggle. Call after IsDoneLogic has been applied.
    /// <para>
    /// The increment is <b>once per cycle</b>, not once per toggle: un-ticking and re-ticking an item, or
    /// re-sending <c>ForceValue = true</c> at an already-complete counted item (which leaves it done and still
    /// runs the after-toggle hook), must not walk <c>Streak</c> and <c>BestStreak</c> up. Only a reset opens the
    /// item up to being counted again. <see cref="RoutineTodoList.LastCompletedAt"/> still moves on every
    /// completing toggle — it means "last completed", not "last scored".
    /// </para>
    /// </summary>
    public static void UpdateItemStreak(RoutineTodoList item, DateTime now)
    {
        if (!item.IsDone)
            return;

        var alreadyCounted = AlreadyCountedThisCycle(item);
        item.LastCompletedAt = now;

        if (alreadyCounted)
            return;

        item.Streak++;
        if (item.Streak > item.BestStreak)
            item.BestStreak = item.Streak;
    }
}