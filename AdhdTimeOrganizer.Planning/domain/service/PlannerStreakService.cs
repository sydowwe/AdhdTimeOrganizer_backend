using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;

namespace AdhdTimeOrganizer.Planning.domain.service;

/// <summary>
/// The day-plan completion streak rules, as a pure function over per-day tallies. Every product decision the
/// streak makes lives here and nowhere else — the reader supplies numbers, the endpoint ships the result.
/// <para>
/// Deliberately <b>derived, never stored</b>. This is the opposite choice from the Routines slice's
/// <c>RoutineResetService</c>, which keeps <c>Streak</c> / <c>BestStreak</c> as columns on
/// <c>RoutineTimePeriod</c> — and the difference is not taste. A routine period <i>wipes its items</i> on
/// reset, so once a period has elapsed there is nothing left to recompute from and the counter has to be the
/// record. Planner days keep every <see cref="PlannerTask"/> row forever, so the streak can be re-derived from
/// source on demand, which buys three things a counter cannot:
/// <list type="bullet">
/// <item>un-ticking a task is an exact inverse, because there is no increment to undo (the client-side store
/// this replaces guessed at the inverse and got it wrong — it rolled the date back exactly one day, with no
/// way to know whether the day before was also complete);</item>
/// <item>editing a past day recomputes the whole walk, including across the gap it opens or closes;</item>
/// <item>the number cannot drift from the rows it claims to describe, so there is no repair path to write.</item>
/// </list>
/// The cost is one grouped query per read — see <c>PlannerStreakReader</c> for why that is bounded by
/// days-with-tasks rather than tasks.
/// </para>
/// </summary>
public static class PlannerStreakService
{
    /// <summary>
    /// One day's planner tasks reduced to the three counts the rules need. Only <b>qualifying</b> tasks are
    /// counted: not <see cref="BasePlannerTask.IsBackground"/> and not <see cref="BasePlannerTask.IsOptional"/>
    /// — see <see cref="Evaluate"/>. A day with no qualifying tasks produces no tally at all.
    /// </summary>
    /// <param name="Date">The day, in the user's own timezone (see <c>PlannerStreakReader</c>).</param>
    /// <param name="Qualifying">Qualifying tasks planned for the day.</param>
    /// <param name="Cancelled">Of those, how many the user skipped.</param>
    /// <param name="Completed">Of those, how many the user completed. Disjoint from <paramref name="Cancelled"/>.</param>
    public readonly record struct PlannerDayTally(DateOnly Date, int Qualifying, int Cancelled, int Completed);

    /// <summary>What one day does to the streak.</summary>
    public enum PlannerDayOutcome
    {
        /// <summary>The day says nothing about the user: it neither extends the streak nor breaks it.</summary>
        Empty,

        /// <summary>Every task that was left to do got done — the streak advances by one.</summary>
        Complete,

        /// <summary>Work was left on the table. Breaks the streak, unless the day is still today.</summary>
        Incomplete
    }

    /// <summary>The three numbers the flame chip needs, plus the day they were computed against.</summary>
    public readonly record struct PlannerStreakSnapshot(int CurrentStreak, int BestStreak, bool IsTodayComplete);

    /// <summary>
    /// What a single day does to the streak. Four product decisions are encoded here, all four settled
    /// against the frontend's B1 ask rather than inherited from the client store this replaces:
    /// <list type="number">
    /// <item><b>Background tasks never count</b> — already the client's rule, confirmed. Out of both the
    /// numerator and the denominator, so a day of nothing but background tasks is <see cref="PlannerDayOutcome.Empty"/>.</item>
    /// <item><b>Optional tasks never count either</b> — new, and not in the ask. A task the user marked
    /// <see cref="BasePlannerTask.IsOptional"/> (importance <see cref="TaskImportance.OptionalMarkerValue"/>)
    /// cannot block a day. Note this makes the streak's denominator <i>narrower</i> than the widget's progress
    /// ring, which counts every non-background task: a day can read 4/5 on the ring and still be complete here.</item>
    /// <item><b>A skip is neutral, not a failure.</b> <see cref="PlannerTaskStatus.Cancelled"/> leaves the
    /// denominator entirely. The app already treats skipping as a legitimate way to close a task — the now-bar
    /// offers reasons for it and the list strikes it through — so ending the streak for it would contradict the
    /// UI silently. Per-reason nuance was considered and rejected: <c>UserPlannerSettings.PredefinedSkipReasons</c>
    /// is a user-editable string list, so the server has no stable vocabulary to grade.</item>
    /// <item><b>But an all-skipped day does not earn a tick.</b> Once every qualifying task is cancelled the
    /// denominator is zero, and this returns <see cref="PlannerDayOutcome.Empty"/> rather than
    /// <see cref="PlannerDayOutcome.Complete"/> — skipping the whole day is not a perfect day. It does not
    /// break the streak either; it simply drops out, exactly like a day that was never planned.</item>
    /// </list>
    /// There are no grace days. The three rules above already make the streak forgiving; a grace allowance on
    /// top would make the displayed number impossible for the user to account for. If one is ever wanted, it
    /// belongs in <see cref="Walk"/>, not here.
    /// </summary>
    public static PlannerDayOutcome Evaluate(PlannerDayTally tally)
    {
        // Skips leave the denominator, so "what was left to do" is qualifying minus cancelled.
        var actionable = tally.Qualifying - tally.Cancelled;

        if (actionable <= 0)
            return PlannerDayOutcome.Empty;

        return tally.Completed >= actionable
            ? PlannerDayOutcome.Complete
            : PlannerDayOutcome.Incomplete;
    }

    /// <summary>
    /// Walks the user's planned days oldest-first and produces the current streak, the best streak ever held,
    /// and whether today is complete. <paramref name="tallies"/> need not be sorted and need not be contiguous
    /// — a date with no entry is a day with no qualifying tasks, which is
    /// <see cref="PlannerDayOutcome.Empty"/> and therefore skipped, so gaps cost nothing.
    /// <para>
    /// <b>Empty days are transparent, not free.</b> They do not break the streak — weekends, holidays and days
    /// the user never planned must not punish them — but they do not add to it either. The number means "days
    /// I completed", so a Monday and a Wednesday with an unplanned Tuesday between them read 2, not 3.
    /// </para>
    /// <para>
    /// <b>Today can never break the streak</b>, only extend it. The day is not over: an incomplete today is
    /// unfinished, not failed. Without this the chip would read 0 every morning and climb back by evening,
    /// which is the single most visible way to make the number look broken.
    /// </para>
    /// <para>
    /// <b>Days after <paramref name="today"/> are ignored outright.</b> Planning tomorrow must not zero the
    /// streak, and a day that has not happened cannot be complete.
    /// </para>
    /// One ascending pass yields both numbers, which is deliberate: the current streak is simply the run still
    /// open when the walk reaches today, so <c>currentStreak &gt; bestStreak</c> is unrepresentable rather than
    /// merely unlikely.
    /// </summary>
    public static PlannerStreakSnapshot Walk(IEnumerable<PlannerDayTally> tallies, DateOnly today)
    {
        var current = 0;
        var best = 0;
        var isTodayComplete = false;

        foreach (var tally in tallies.Where(t => t.Date <= today).OrderBy(t => t.Date))
        {
            var outcome = Evaluate(tally);
            var isToday = tally.Date == today;

            if (isToday)
                isTodayComplete = outcome == PlannerDayOutcome.Complete;

            switch (outcome)
            {
                case PlannerDayOutcome.Complete:
                    current++;
                    if (current > best)
                        best = current;
                    break;

                case PlannerDayOutcome.Incomplete when !isToday:
                    current = 0;
                    break;

                // Empty days, and an incomplete today, leave the run exactly where it was.
                case PlannerDayOutcome.Incomplete:
                case PlannerDayOutcome.Empty:
                default:
                    break;
            }
        }

        return new PlannerStreakSnapshot(current, best, isTodayComplete);
    }
}
