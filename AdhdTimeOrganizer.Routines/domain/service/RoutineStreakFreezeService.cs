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
        // An empty period is not a miss either, even though IsSuccess says false for it: RecomputeStreak skips
        // TotalCount == 0 rows outright and TryReset leaves the streak NotEvaluated, so such a period already
        // carries the run for free and a freeze on it would be charged for nothing.
        // An excused cycle (the user was away for most of it) already carries the run for free, exactly like an
        // empty one, so it is not a miss either — and the CHECK forbids a row being both.
        if (!IsMiss(entry, period.StreakThreshold))
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
    /// matching <see cref="RoutineResetService.TryReset"/>, which leaves the streak untouched in that case — and
    /// so does an excused one (<see cref="RoutinePeriodCompletion.IsExcused"/>, the user was away), for the same
    /// reason a freeze does.
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
        var (streak, best) = Walk(historyAsc, period.StreakThreshold, alsoFrozen: null);

        period.Streak = Math.Max(period.Streak, streak);
        period.BestStreak = Math.Max(period.BestStreak, Math.Max(best, period.Streak));
    }

    /// <summary>
    /// What a reset that broke the streak cost, and whether a freeze could still buy it back — for the
    /// "streak broken" notification, which otherwise can only say "broken" (the streak it carries is already 0).
    /// </summary>
    /// <param name="LostStreak">The run the newest miss ended: the trailing run of the history before it.</param>
    /// <param name="FreezesRemaining">The budget as <see cref="RefreshFreezeBudget"/> would leave it now.</param>
    /// <param name="CanFreeze">
    /// <see cref="Apply"/> on the newest entry would succeed now <b>and</b> raise the streak — so the
    /// notification can offer it without promising a rescue the endpoint would refuse.
    /// </param>
    public readonly record struct StreakBreak(int LostStreak, int FreezesRemaining, bool CanFreeze);

    /// <summary>
    /// Pure, unlike <see cref="Apply"/>: answers what spending a freeze on the newest entry of
    /// <paramref name="historyAsc"/> would do, without spending it or refilling the budget. Same rules as
    /// <see cref="Apply"/> (an unfrozen, non-excused, non-empty miss, and budget left) and the same walk as
    /// <see cref="RecomputeStreak"/>, so the offer and the endpoint cannot disagree.
    /// <para>
    /// <paramref name="historyAsc"/> must be the <b>complete</b> history, ascending, including the miss just
    /// written — the same contract <see cref="Apply"/> has.
    /// </para>
    /// </summary>
    public static StreakBreak DescribeBreak(RoutineTimePeriod period, IReadOnlyList<RoutinePeriodCompletion> historyAsc, DateTime now)
    {
        var freezesLeft = period.FreezeBudgetResetsAt is { } resetsAt && now < resetsAt
            ? period.FreezesRemaining
            : period.FreezeBudget;

        if (historyAsc.Count == 0)
            return new StreakBreak(0, freezesLeft, CanFreeze: false);

        var newest = historyAsc[^1];
        var (restored, _) = Walk(historyAsc, period.StreakThreshold, alsoFrozen: newest);

        var canFreeze = !newest.IsFrozen
                        && IsMiss(newest, period.StreakThreshold)
                        && freezesLeft > 0
                        && restored > period.Streak;

        return new StreakBreak(restored, freezesLeft, canFreeze);
    }

    /// <summary>
    /// A cycle a freeze can cover. An excused or empty one already carries the run for free, and a met one has
    /// nothing to cover — see <see cref="Apply"/>.
    /// </summary>
    private static bool IsMiss(RoutinePeriodCompletion completion, int streakThreshold) =>
        !completion.IsExcused && completion.TotalCount > 0 && !IsSuccess(completion, streakThreshold);

    /// <summary>
    /// The trailing run and the best run of <paramref name="historyAsc"/>, reading <paramref name="alsoFrozen"/>
    /// as frozen whatever its flag says — which is how <see cref="DescribeBreak"/> asks "what if" without
    /// touching the entity.
    /// </summary>
    private static (int Streak, int Best) Walk(
        IReadOnlyList<RoutinePeriodCompletion> historyAsc, int streakThreshold, RoutinePeriodCompletion? alsoFrozen)
    {
        var streak = 0;
        var best = 0;

        foreach (var completion in historyAsc)
        {
            if (completion.IsFrozen || ReferenceEquals(completion, alsoFrozen) || completion.IsExcused || completion.TotalCount == 0)
                continue;

            if (IsSuccess(completion, streakThreshold))
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

        return (streak, best);
    }
}
