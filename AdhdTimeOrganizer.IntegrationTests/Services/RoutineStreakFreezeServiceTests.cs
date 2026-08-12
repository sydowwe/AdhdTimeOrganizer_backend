using AdhdTimeOrganizer.Routines.domain.model.@enum;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Services;

/// <summary>
/// The freeze rules, in isolation from the endpoint. These are the cases that decide whether a user's run
/// survives, and none of them are observable from a status code.
/// </summary>
public class RoutineStreakFreezeServiceTests
{
    private static RoutineTimePeriod MakePeriod(int streakThreshold = 100, int budget = 2, int remaining = 2) =>
        new()
        {
            UserId = 1,
            Text = "Test",
            Color = "#000000",
            LengthInDays = 7,
            ResetAnchorDay = 0,
            StreakThreshold = streakThreshold,
            StreakGraceDays = 0,
            FreezeBudget = budget,
            FreezesRemaining = remaining
        };

    private static RoutinePeriodCompletion MakeCompletion(
        int startDayOfYear, int completed, int total, bool isFrozen = false) =>
        new()
        {
            TimePeriodId = 1,
            PeriodStart = new DateOnly(2026, 1, 1).AddDays(startDayOfYear),
            PeriodEnd = new DateOnly(2026, 1, 1).AddDays(startDayOfYear + 7),
            CompletedCount = completed,
            TotalCount = total,
            IsFrozen = isFrozen,
            FrozenAt = isFrozen ? new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc) : null
        };

    private static readonly DateTime Now = new(2026, 2, 10, 12, 0, 0, DateTimeKind.Utc);

    // ─── RefreshFreezeBudget ─────────────────────────────────────────────────

    [Fact]
    public void RefreshFreezeBudget_InsideTheWindow_ChangesNothing()
    {
        var period = MakePeriod(remaining: 1);
        period.FreezeBudgetResetsAt = Now.AddDays(3);

        RoutineStreakFreezeService.RefreshFreezeBudget(period, Now).Should().BeFalse();
        period.FreezesRemaining.Should().Be(1);
    }

    [Fact]
    public void RefreshFreezeBudget_AfterTheWindow_RefillsAndOpensTheNextOne()
    {
        var period = MakePeriod(remaining: 0);
        period.FreezeBudgetResetsAt = Now.AddDays(-1);

        RoutineStreakFreezeService.RefreshFreezeBudget(period, Now).Should().BeTrue();

        period.FreezesRemaining.Should().Be(2);
        period.FreezeBudgetResetsAt.Should().Be(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    /// <summary>A row that predates the feature has no window, and must be treated as due rather than stuck.</summary>
    [Fact]
    public void RefreshFreezeBudget_WithNoWindowYet_OpensOne()
    {
        var period = MakePeriod(remaining: 0);
        period.FreezeBudgetResetsAt = null;

        RoutineStreakFreezeService.RefreshFreezeBudget(period, Now).Should().BeTrue();
        period.FreezesRemaining.Should().Be(2);
        period.FreezeBudgetResetsAt.Should().Be(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void NextBudgetReset_InDecember_RollsIntoTheNextYear()
    {
        RoutineStreakFreezeService.NextBudgetReset(new DateTime(2026, 12, 20, 8, 0, 0, DateTimeKind.Utc))
            .Should().Be(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    // ─── RecomputeStreak ─────────────────────────────────────────────────────

    /// <summary>
    /// The core of the feature: a frozen period carries the run rather than extending it. If it extended the
    /// run, a freeze would be worth more than actually doing the routine.
    /// </summary>
    [Fact]
    public void RecomputeStreak_FrozenPeriodCarriesTheRun_WithoutExtendingIt()
    {
        var period = MakePeriod();
        var history = new[]
        {
            MakeCompletion(0, 3, 3),
            MakeCompletion(7, 3, 3),
            MakeCompletion(14, 0, 3, isFrozen: true),
            MakeCompletion(21, 3, 3)
        };

        RoutineStreakFreezeService.RecomputeStreak(period, history);

        period.Streak.Should().Be(3, "two before the freeze plus one after — the freeze itself does not count");
    }

    [Fact]
    public void RecomputeStreak_UnfrozenMiss_BreaksTheRun()
    {
        var period = MakePeriod();
        var history = new[]
        {
            MakeCompletion(0, 3, 3),
            MakeCompletion(7, 3, 3),
            MakeCompletion(14, 1, 3),
            MakeCompletion(21, 3, 3)
        };

        RoutineStreakFreezeService.RecomputeStreak(period, history);

        period.Streak.Should().Be(1);
        period.BestStreak.Should().Be(2, "the run before the break is still the best one on record");
    }

    /// <summary>
    /// An empty period says nothing about the routine — <c>TryReset</c> leaves the streak alone in that case,
    /// so the walk has to agree or a routine emptied for a week would lose its run on the next freeze.
    /// </summary>
    [Fact]
    public void RecomputeStreak_PeriodWithNoItems_CarriesTheRun()
    {
        var period = MakePeriod();
        var history = new[] { MakeCompletion(0, 0, 0), MakeCompletion(7, 3, 3) };

        RoutineStreakFreezeService.RecomputeStreak(period, history);

        period.Streak.Should().Be(1);
    }

    /// <summary>
    /// History is finite; a streak can predate its oldest row. Spending a freeze must never be the thing that
    /// cuts a run down.
    /// </summary>
    [Fact]
    public void RecomputeStreak_NeverLowersAnExistingStreak()
    {
        var period = MakePeriod();
        period.Streak = 40;
        period.BestStreak = 40;

        RoutineStreakFreezeService.RecomputeStreak(period, new[] { MakeCompletion(0, 3, 3) });

        period.Streak.Should().Be(40);
        period.BestStreak.Should().Be(40);
    }

    [Fact]
    public void RecomputeStreak_HonoursAPartialThreshold()
    {
        var period = MakePeriod(streakThreshold: 60);
        var history = new[] { MakeCompletion(0, 2, 3), MakeCompletion(7, 2, 3) };

        RoutineStreakFreezeService.RecomputeStreak(period, history);

        period.Streak.Should().Be(2, "2 of 3 is 66%, which clears a 60% threshold");
    }

    // ─── Apply ───────────────────────────────────────────────────────────────

    [Fact]
    public void Apply_OnTheNewestMiss_SpendsAFreeze_ClearsGrace_AndRestoresTheStreak()
    {
        var period = MakePeriod();
        period.Streak = 0;
        period.StreakGraceUntil = Now.AddDays(2);
        period.GraceNotifiedFor = Now.AddDays(2);
        period.FreezeBudgetResetsAt = Now.AddDays(10);

        var miss = MakeCompletion(14, 1, 3);
        var history = new[] { MakeCompletion(0, 3, 3), MakeCompletion(7, 3, 3), miss };

        RoutineStreakFreezeService.Apply(period, history, miss, Now).Should().Be(StreakFreezeRejection.None);

        miss.IsFrozen.Should().BeTrue();
        miss.FrozenAt.Should().Be(Now);
        period.FreezesRemaining.Should().Be(1);
        period.Streak.Should().Be(2);
        period.StreakGraceUntil.Should().BeNull("the freeze covered the very miss that opened the window");
        period.GraceNotifiedFor.Should().BeNull();
    }

    /// <summary>
    /// Freezing an older miss leaves an open grace window alone — that window belongs to the newest period,
    /// which is still unresolved.
    /// </summary>
    [Fact]
    public void Apply_OnAnOlderMiss_LeavesTheOpenGraceWindowAlone()
    {
        var period = MakePeriod();
        var graceUntil = Now.AddDays(2);
        period.StreakGraceUntil = graceUntil;
        period.FreezeBudgetResetsAt = Now.AddDays(10);

        var olderMiss = MakeCompletion(7, 1, 3);
        var history = new[] { MakeCompletion(0, 3, 3), olderMiss, MakeCompletion(14, 0, 3) };

        RoutineStreakFreezeService.Apply(period, history, olderMiss, Now).Should().Be(StreakFreezeRejection.None);

        olderMiss.IsFrozen.Should().BeTrue();
        period.StreakGraceUntil.Should().Be(graceUntil);
    }

    [Fact]
    public void Apply_WithNoBudget_RefusesAndWritesNothing()
    {
        var period = MakePeriod(remaining: 0);
        period.FreezeBudgetResetsAt = Now.AddDays(10);

        var miss = MakeCompletion(0, 1, 3);

        RoutineStreakFreezeService.Apply(period, new[] { miss }, miss, Now)
            .Should().Be(StreakFreezeRejection.NoBudget);

        miss.IsFrozen.Should().BeFalse();
        miss.FrozenAt.Should().BeNull();
    }

    [Fact]
    public void Apply_OnAnAlreadyFrozenPeriod_Refuses()
    {
        var period = MakePeriod();
        var frozen = MakeCompletion(0, 1, 3, isFrozen: true);

        RoutineStreakFreezeService.Apply(period, new[] { frozen }, frozen, Now)
            .Should().Be(StreakFreezeRejection.AlreadyFrozen);

        period.FreezesRemaining.Should().Be(2);
    }

    [Fact]
    public void Apply_OnAPeriodThatMetItsThreshold_Refuses()
    {
        var period = MakePeriod();
        var met = MakeCompletion(0, 3, 3);

        RoutineStreakFreezeService.Apply(period, new[] { met }, met, Now)
            .Should().Be(StreakFreezeRejection.NotAMiss);

        period.FreezesRemaining.Should().Be(2);
        met.IsFrozen.Should().BeFalse();
    }

    /// <summary>
    /// A zero budget is a real configuration — "this period grants no freezes" — and must refuse rather than
    /// fall through to a refill that hands out a budget the period does not have.
    /// </summary>
    [Fact]
    public void Apply_OnAPeriodWithAZeroBudget_Refuses()
    {
        var period = MakePeriod(budget: 0, remaining: 0);
        var miss = MakeCompletion(0, 1, 3);

        RoutineStreakFreezeService.Apply(period, new[] { miss }, miss, Now)
            .Should().Be(StreakFreezeRejection.NoBudget);

        miss.IsFrozen.Should().BeFalse();
    }
}
