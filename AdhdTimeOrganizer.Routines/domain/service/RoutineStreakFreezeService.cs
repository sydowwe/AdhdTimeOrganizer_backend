using AdhdTimeOrganizer.Routines.domain.model.@enum;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.Routines.domain.service;

/// <summary>
/// Streak freezes: a budgeted way to keep a run alive across a period the user missed outright.
/// <para>
/// Distinct from <see cref="RoutineTimePeriod.StreakGraceDays"/>, which is a <i>time-based</i> leniency — finish
/// late, keep the run. A freeze is the <i>budgeted</i> one — do not finish at all, keep the run — and the two
/// stack deliberately: grace is what the period gives away for free and expires on its own, a freeze is what the
/// user spends when grace has already lapsed. Folding them into one another would mean either charging for
/// lateness or handing out unlimited skips.
/// </para>
/// <para>
/// Static and pure, like <see cref="RoutineResetService"/> — every method mutates the entities it is handed and
/// leaves persisting them to the caller, so a freeze can share the transaction that reads the period.
/// </para>
/// </summary>
public static class RoutineStreakFreezeService
{
    /// <summary>Freezes a period grants per window unless the user says otherwise.</summary>
    public const int DefaultFreezeBudget = 2;

    /// <summary>
    /// Ceiling on <see cref="RoutineTimePeriod.FreezeBudget"/>, mirrored by
    /// <c>ck_routine_time_period_freeze_budget_range</c>. A budget approaching the history depth would let every
    /// miss be covered, at which point the streak is decoration.
    /// </summary>
    public const int MaxFreezeBudget = 10;

    /// <summary>Midnight UTC on the first of the month after <paramref name="now"/>.</summary>
    public static DateTime NextBudgetReset(DateTime now)
    {
        var utc = now.Kind == DateTimeKind.Utc ? now : now.ToUniversalTime();
        return new DateTime(utc.Year, utc.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
    }

    /// <summary>
    /// Refills the budget if its window has elapsed. Call this before reading <em>or</em> spending
    /// <see cref="RoutineTimePeriod.FreezesRemaining"/> — there is no scheduled job behind it, so a surface that
    /// skips the call serves a stale count and, worse, refuses a freeze the user is entitled to.
    /// <para>A null <see cref="RoutineTimePeriod.FreezeBudgetResetsAt"/> means no window has opened yet (a row
    /// that predates the feature, or a freshly created period) and is treated as due.</para>
    /// Returns true if the period was modified.
    /// </summary>
    public static bool RefreshFreezeBudget(RoutineTimePeriod period, DateTime now)
    {
        if (period.FreezeBudgetResetsAt is { } resetsAt && now < resetsAt)
            return false;

        var next = NextBudgetReset(now);
        if (period.FreezesRemaining == period.FreezeBudget && period.FreezeBudgetResetsAt == next)
            return false;

        period.FreezesRemaining = period.FreezeBudget;
        period.FreezeBudgetResetsAt = next;
        return true;
    }

    /// <summary>
    /// Whether a completion row cleared its period's threshold. A period that elapsed with no items at all
    /// (<c>TotalCount == 0</c>) is neither — see <see cref="RecomputeStreak"/>.
    /// </summary>
    public static bool IsSuccess(RoutinePeriodCompletion completion, int streakThreshold) =>
        completion.TotalCount > 0 &&
        (double)completion.CompletedCount / completion.TotalCount * 100.0 >= streakThreshold;

    /// <summary>
    /// Spends one freeze on <paramref name="entry"/>, then re-derives the streak from the history.
    /// <paramref name="historyAsc"/> must be the period's <b>complete</b> completion history ordered by
    /// <see cref="RoutinePeriodCompletion.PeriodStart"/> ascending, and must contain <paramref name="entry"/>
    /// itself — the streak is a property of the run, so a truncated history would under-count it.
    /// <para>Returns <see cref="StreakFreezeRejection.None"/> and mutates on success; on any other value nothing
    /// was written.</para>
    /// </summary>
    public static StreakFreezeRejection Apply(
        RoutineTimePeriod period,
        IReadOnlyList<RoutinePeriodCompletion> historyAsc,
        RoutinePeriodCompletion entry,
        DateTime now)
    {
        if (entry.IsFrozen)
            return StreakFreezeRejection.AlreadyFrozen;

        // Nothing to cover. Allowing it would burn a freeze for no effect, and the client only ever offers the
        // action on a miss — so reaching here means a stale view, which deserves an error rather than a charge.
        if (IsSuccess(entry, period.StreakThreshold))
            return StreakFreezeRejection.NotAMiss;

        RefreshFreezeBudget(period, now);
        if (period.FreezesRemaining <= 0)
            return StreakFreezeRejection.NoBudget;

        period.FreezesRemaining--;
        entry.IsFrozen = true;
        entry.FrozenAt = now;

        // The freeze covers the miss that opened this grace window, so the window has nothing left to protect.
        // Only the newest entry can own the open window; freezing an older one leaves it alone.
        if (ReferenceEquals(entry, historyAsc[^1]))
        {
            period.StreakGraceUntil = null;
            period.GraceNotifiedFor = null;
        }

        RecomputeStreak(period, historyAsc);
        return StreakFreezeRejection.None;
    }

    /// <summary>
    /// Re-derives <see cref="RoutineTimePeriod.Streak"/> / <see cref="RoutineTimePeriod.BestStreak"/> by walking
    /// the completion history oldest to newest.
    /// <para>
    /// A frozen period <i>carries</i> the run rather than extending it: the point of a freeze is that the streak
    /// survives, not that a skipped period counts as done. A period that elapsed with no items carries it too,
    /// matching <see cref="RoutineResetService.TryReset"/>, which leaves the streak untouched in that case.
    /// </para>
    /// <para>
    /// Both values are only ever raised, never lowered. The walk sees only what the history holds, and history
    /// is finite — a period whose streak predates its oldest completion row would otherwise be cut down by an
    /// operation the user spent a resource on to protect it. Correcting a streak downward is not this method's
    /// job.
    /// </para>
    /// </summary>
    public static void RecomputeStreak(RoutineTimePeriod period, IReadOnlyList<RoutinePeriodCompletion> historyAsc)
    {
        var streak = 0;
        var best = 0;

        foreach (var completion in historyAsc)
        {
            if (completion.IsFrozen || completion.TotalCount == 0)
                continue;

            if (IsSuccess(completion, period.StreakThreshold))
            {
                streak++;
                if (streak > best)
                    best = streak;
            }
            else
            {
                streak = 0;
            }
        }

        period.Streak = Math.Max(period.Streak, streak);
        period.BestStreak = Math.Max(period.BestStreak, Math.Max(best, period.Streak));
    }
}
