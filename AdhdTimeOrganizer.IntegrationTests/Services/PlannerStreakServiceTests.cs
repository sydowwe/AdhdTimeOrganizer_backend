using AdhdTimeOrganizer.Planning.domain.service;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Services;

/// <summary>
/// The day-plan streak rules, exercised without a database — the whole rule set is a pure function over
/// per-day tallies, and every product decision in it is a decision someone could plausibly reverse by
/// accident later. Each test below names the decision it pins.
/// <para>
/// The end-to-end half (that the numbers actually reach the client off the plan response, and that
/// un-ticking a task moves them) is <c>Endpoints.PlannerStreakTests</c>.
/// </para>
/// </summary>
public class PlannerStreakServiceTests
{
    private static readonly DateOnly Today = new(2026, 8, 13);

    private static PlannerStreakService.PlannerDayTally Day(int daysAgo, int qualifying, int completed, int cancelled = 0) =>
        new(Today.AddDays(-daysAgo), qualifying, cancelled, completed);

    // ─── Evaluate: what one day is worth ─────────────────────────────────────

    [Fact]
    public void Evaluate_AllQualifyingTasksCompleted_IsComplete()
    {
        PlannerStreakService.Evaluate(Day(0, qualifying: 3, completed: 3))
            .Should().Be(PlannerStreakService.PlannerDayOutcome.Complete);
    }

    [Fact]
    public void Evaluate_OneTaskLeftUndone_IsIncomplete()
    {
        PlannerStreakService.Evaluate(Day(0, qualifying: 3, completed: 2))
            .Should().Be(PlannerStreakService.PlannerDayOutcome.Incomplete);
    }

    /// <summary>
    /// A skip is neutral: it leaves the denominator entirely, so completing everything that was left counts
    /// as a complete day. This is the decision the frontend flagged as mattering most — the client's rule was
    /// the opposite (a skip ended the streak permanently) while the rest of the app presented skipping as a
    /// legitimate way to close a task.
    /// </summary>
    [Fact]
    public void Evaluate_SkippedTask_DoesNotBlockTheDay()
    {
        PlannerStreakService.Evaluate(Day(0, qualifying: 3, completed: 2, cancelled: 1))
            .Should().Be(PlannerStreakService.PlannerDayOutcome.Complete);
    }

    /// <summary>
    /// But skipping is not a way to <i>earn</i> a day. With everything cancelled the denominator is zero, and
    /// the day drops out as Empty rather than counting as perfect.
    /// </summary>
    [Fact]
    public void Evaluate_EveryTaskSkipped_IsEmptyNotComplete()
    {
        PlannerStreakService.Evaluate(Day(0, qualifying: 3, completed: 0, cancelled: 3))
            .Should().Be(PlannerStreakService.PlannerDayOutcome.Empty);
    }

    [Fact]
    public void Evaluate_NoQualifyingTasks_IsEmpty()
    {
        PlannerStreakService.Evaluate(Day(0, qualifying: 0, completed: 0))
            .Should().Be(PlannerStreakService.PlannerDayOutcome.Empty);
    }

    // ─── Walk: how days compose ──────────────────────────────────────────────

    [Fact]
    public void Walk_ConsecutiveCompleteDays_Accumulate()
    {
        var snapshot = PlannerStreakService.Walk(
            [Day(2, 2, 2), Day(1, 1, 1), Day(0, 3, 3)], Today);

        snapshot.CurrentStreak.Should().Be(3);
        snapshot.BestStreak.Should().Be(3);
        snapshot.IsTodayComplete.Should().BeTrue();
    }

    [Fact]
    public void Walk_IncompletePastDay_BreaksTheStreak()
    {
        var snapshot = PlannerStreakService.Walk(
            [Day(3, 2, 2), Day(2, 2, 1), Day(1, 1, 1), Day(0, 1, 1)], Today);

        snapshot.CurrentStreak.Should().Be(2, "the run restarts after the missed day");
        snapshot.BestStreak.Should().Be(2);
    }

    /// <summary>
    /// An unplanned day — a weekend, a holiday, or simply a day the user never opened the planner — must not
    /// punish them. It is transparent: the run survives it.
    /// </summary>
    [Fact]
    public void Walk_EmptyDay_DoesNotBreakTheStreak()
    {
        var snapshot = PlannerStreakService.Walk(
            [Day(2, 2, 2), Day(0, 2, 2)], Today);

        snapshot.CurrentStreak.Should().Be(2);
    }

    /// <summary>
    /// ...but it does not pay either. Two completed days with a gap between them read 2, not 3 — the number
    /// means "days I completed", not "days since I last failed".
    /// </summary>
    [Fact]
    public void Walk_EmptyDay_DoesNotItselfCount()
    {
        var withGap = PlannerStreakService.Walk([Day(2, 2, 2), Day(0, 2, 2)], Today);
        var contiguous = PlannerStreakService.Walk([Day(2, 2, 2), Day(1, 2, 2), Day(0, 2, 2)], Today);

        withGap.CurrentStreak.Should().Be(2);
        contiguous.CurrentStreak.Should().Be(3);
    }

    /// <summary>
    /// A long silence bridges rather than breaks. Recorded because it is the deliberate cost of the rule
    /// above: someone who plans nothing for a month and then completes a day resumes their old run.
    /// </summary>
    [Fact]
    public void Walk_LongGapOfUnplannedDays_Bridges()
    {
        var snapshot = PlannerStreakService.Walk(
            [Day(90, 2, 2), Day(89, 2, 2), Day(0, 2, 2)], Today);

        snapshot.CurrentStreak.Should().Be(3);
    }

    /// <summary>
    /// The single most visible failure mode if this is got wrong: an unfinished today is not a failed today,
    /// so the chip must not read 0 every morning and climb back by evening.
    /// </summary>
    [Fact]
    public void Walk_IncompleteToday_DoesNotBreakTheStreak()
    {
        var snapshot = PlannerStreakService.Walk(
            [Day(2, 2, 2), Day(1, 2, 2), Day(0, 4, 1)], Today);

        snapshot.CurrentStreak.Should().Be(2);
        snapshot.IsTodayComplete.Should().BeFalse();
    }

    [Fact]
    public void Walk_FutureDays_AreIgnored()
    {
        var snapshot = PlannerStreakService.Walk(
            [Day(1, 2, 2), Day(0, 2, 2), Day(-1, 5, 0)], Today);

        snapshot.CurrentStreak.Should().Be(2, "planning tomorrow cannot break today's streak");
        snapshot.BestStreak.Should().Be(2);
    }

    [Fact]
    public void Walk_BestStreak_SurvivesTheStreakBreaking()
    {
        var snapshot = PlannerStreakService.Walk(
            [Day(5, 1, 1), Day(4, 1, 1), Day(3, 1, 1), Day(2, 2, 0), Day(1, 1, 1), Day(0, 1, 1)], Today);

        snapshot.CurrentStreak.Should().Be(2);
        snapshot.BestStreak.Should().Be(3);
    }

    /// <summary>
    /// Both numbers come out of one ascending pass precisely so this cannot happen. Asserted anyway, because
    /// a client rendering "4 (best 3)" is the kind of thing a user screenshots.
    /// </summary>
    [Fact]
    public void Walk_CurrentStreak_NeverExceedsBest()
    {
        var snapshot = PlannerStreakService.Walk(
            [Day(2, 1, 1), Day(1, 1, 1), Day(0, 1, 1)], Today);

        snapshot.CurrentStreak.Should().BeLessThanOrEqualTo(snapshot.BestStreak);
    }

    [Fact]
    public void Walk_NoHistoryAtAll_IsZero()
    {
        var snapshot = PlannerStreakService.Walk([], Today);

        snapshot.CurrentStreak.Should().Be(0);
        snapshot.BestStreak.Should().Be(0);
        snapshot.IsTodayComplete.Should().BeFalse();
    }

    [Fact]
    public void Walk_UnorderedInput_IsSortedBeforeWalking()
    {
        var snapshot = PlannerStreakService.Walk(
            [Day(0, 1, 1), Day(2, 2, 0), Day(1, 1, 1)], Today);

        snapshot.CurrentStreak.Should().Be(2);
    }

    /// <summary>
    /// The un-tick case, which is the defect this whole feature exists to fix. The client store decremented
    /// the counter and rolled its stored date back exactly one day — a guess, and wrong whenever the day
    /// before was also complete. Here it is not a mutation at all: the same days with one task un-ticked are
    /// simply a different input, and re-ticking returns the identical number.
    /// </summary>
    [Fact]
    public void Walk_UntickingAndRetickingATask_IsExactlyReversible()
    {
        PlannerStreakService.PlannerDayTally[] ticked = [Day(2, 2, 2), Day(1, 2, 2), Day(0, 2, 2)];
        PlannerStreakService.PlannerDayTally[] unticked = [Day(2, 2, 2), Day(1, 2, 1), Day(0, 2, 2)];

        PlannerStreakService.Walk(ticked, Today).CurrentStreak.Should().Be(3);
        PlannerStreakService.Walk(unticked, Today).CurrentStreak.Should().Be(1, "the broken day now ends the run");
        PlannerStreakService.Walk(ticked, Today).CurrentStreak.Should().Be(3, "re-ticking restores it exactly");
    }
}
