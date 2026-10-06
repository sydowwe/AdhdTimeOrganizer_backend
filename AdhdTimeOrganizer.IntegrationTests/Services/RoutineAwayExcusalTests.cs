using AdhdTimeOrganizer.Routines.domain.model.@enum;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Services;

/// <summary>
/// FO1 — a cycle the user was mostly away for (vacation or sick) does not break the routine. Pure: the away
/// days are handed in as a set, exactly as the reset sites pre-read them. The rule is owner-decided
/// (2026-10-06): at least half of the cycle's days, so a three-day flu inside a week is not excused.
/// </summary>
public class RoutineAwayExcusalTests
{
    // Monday. A weekly period reset here closes [Jan 5, Jan 12) at Jan 12 00:00 UTC.
    private static readonly DateTime Jan5 = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Jan5Date = DateOnly.FromDateTime(Jan5);

    private static RoutineTimePeriod MakePeriod(int lengthInDays = 7, int graceDays = 0, int streak = 5) =>
        new()
        {
            UserId = 1,
            Text = "Test",
            Color = "#000000",
            LengthInDays = lengthInDays,
            ResetAnchorDay = 0,
            StreakThreshold = 100,
            StreakGraceDays = graceDays,
            LastResetAt = Jan5,
            Streak = streak,
            BestStreak = streak
        };

    private static RoutineTodoList MakeItem(bool isDone = false, int streak = 0) =>
        new() { UserId = 1, TimePeriodId = 1, IsDone = isDone, Streak = streak, BestStreak = streak };

    private static HashSet<DateOnly> Days(params int[] offsetsFromJan5) =>
        offsetsFromJan5.Select(o => Jan5Date.AddDays(o)).ToHashSet();

    // ─── The rule ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(new[] { 0, 1, 2 }, false)]       // three of seven: the flu inside a week
    [InlineData(new[] { 0, 1, 2, 3 }, true)]     // four of seven: at least half
    [InlineData(new[] { 0, 1, 2, 3, 4, 5, 6 }, true)]
    [InlineData(new int[0], false)]
    public void IsMostlyAway_IsAtLeastHalfTheCycle(int[] awayOffsets, bool expected)
    {
        RoutineAwayRule.IsMostlyAway(Days(awayOffsets), Jan5Date, Jan5Date.AddDays(7)).Should().Be(expected);
    }

    /// <summary>
    /// <c>PeriodEnd</c> is the boundary that opened the next cycle, so the day it names belongs to the next cycle.
    /// Counting it would excuse a daily routine for the day <i>after</i> a sick day.
    /// </summary>
    [Fact]
    public void IsMostlyAway_EndIsExclusive()
    {
        RoutineAwayRule.IsMostlyAway(Days(1), Jan5Date, Jan5Date.AddDays(1)).Should().BeFalse();
        RoutineAwayRule.IsMostlyAway(Days(0), Jan5Date, Jan5Date.AddDays(1)).Should().BeTrue();
    }

    // ─── At reset ────────────────────────────────────────────────────────────

    [Fact]
    public void TryReset_MissedCycleMostlyAway_IsExcused_HoldsTheStreak_AndRecordsIt()
    {
        var period = MakePeriod(streak: 5);
        var items = new List<RoutineTodoList> { MakeItem(streak: 9), MakeItem(isDone: true, streak: 3) };

        var result = RoutineResetService.TryReset(period, items, Jan5.AddDays(7).AddHours(1), Days(0, 1, 2, 3, 4));

        result.Should().NotBeNull();
        result!.Value.Outcome.Should().Be(StreakOutcome.Excused);
        result.Value.Completion.IsExcused.Should().BeTrue();
        result.Value.Completion.IsFrozen.Should().BeFalse();
        // The counts stay honest: excusal changes scoring, not what happened.
        result.Value.Completion.CompletedCount.Should().Be(1);
        result.Value.Completion.TotalCount.Should().Be(2);
        period.Streak.Should().Be(5, "an excused cycle holds the run, it does not extend it");
        items[0].Streak.Should().Be(9, "an undone item keeps its streak through an away cycle");
        items[1].Streak.Should().Be(3);
    }

    [Fact]
    public void TryReset_MissedCycleThreeOfSevenAway_IsStillBroken()
    {
        var period = MakePeriod(streak: 5);
        var item = MakeItem(streak: 9);

        var result = RoutineResetService.TryReset(period, [item], Jan5.AddDays(7).AddHours(1), Days(0, 1, 2));

        result!.Value.Outcome.Should().Be(StreakOutcome.Broken);
        result.Value.Completion.IsExcused.Should().BeFalse();
        period.Streak.Should().Be(0);
        item.Streak.Should().Be(0);
    }

    /// <summary>Excusal never turns a cycle the user actually met into anything else.</summary>
    [Fact]
    public void TryReset_MetCycle_IsExtended_EvenWhenAllAway()
    {
        var period = MakePeriod(streak: 5);

        var result = RoutineResetService.TryReset(
            period, [MakeItem(isDone: true)], Jan5.AddDays(7).AddHours(1), Days(0, 1, 2, 3, 4, 5, 6));

        result!.Value.Outcome.Should().Be(StreakOutcome.Extended);
        result.Value.Completion.IsExcused.Should().BeFalse();
        period.Streak.Should().Be(6);
    }

    /// <summary>An empty cycle already carries the run for free; marking it excused would say nothing.</summary>
    [Fact]
    public void TryReset_EmptyCycle_IsNotEvaluated_NotExcused()
    {
        var period = MakePeriod();

        var result = RoutineResetService.TryReset(period, [], Jan5.AddDays(7).AddHours(1), Days(0, 1, 2, 3, 4, 5, 6));

        result!.Value.Outcome.Should().Be(StreakOutcome.NotEvaluated);
        result.Value.Completion.IsExcused.Should().BeFalse();
    }

    /// <summary>
    /// No grace window opens for an excused cycle — there is no miss to finish late — but one an earlier, real
    /// miss opened is left to run.
    /// </summary>
    [Fact]
    public void TryReset_Excused_OpensNoGraceWindow_AndLeavesAnOpenOneAlone()
    {
        var fresh = MakePeriod(graceDays: 3);
        RoutineResetService.TryReset(fresh, [MakeItem()], Jan5.AddDays(7).AddHours(1), Days(0, 1, 2, 3, 4, 5, 6));
        fresh.StreakGraceUntil.Should().BeNull();

        var withWindow = MakePeriod(graceDays: 3);
        var open = Jan5.AddDays(2);
        withWindow.StreakGraceUntil = open;
        RoutineResetService.TryReset(withWindow, [MakeItem()], Jan5.AddDays(7).AddHours(1), Days(0, 1, 2, 3, 4, 5, 6));
        withWindow.StreakGraceUntil.Should().Be(open);
    }

    /// <summary>A fortnight away from a daily routine: every cycle excused, the streak intact, in one catch-up.</summary>
    [Fact]
    public void TryResetCatchUp_DailyRoutineThroughAFortnightAway_KeepsTheStreak()
    {
        var period = MakePeriod(lengthInDays: 1, streak: 12);
        var item = MakeItem(streak: 12);
        var away = Days(Enumerable.Range(0, 14).ToArray());

        var result = RoutineResetService.TryResetCatchUp(period, [item], Jan5.AddDays(14).AddHours(1), away);

        result!.Value.CyclesClosed.Should().Be(14);
        result.Value.Completions.Should().OnlyContain(c => c.IsExcused);
        result.Value.Latest.Outcome.Should().Be(StreakOutcome.Excused);
        period.Streak.Should().Be(12);
        item.Streak.Should().Be(12);
    }

    [Fact]
    public void TryReset_SingleItem_HoldsTheItemStreak_OnlyWhenAway()
    {
        var awayItem = MakeItem(streak: 4);
        RoutineResetService.TryReset(MakePeriod(lengthInDays: 1), awayItem, Jan5.AddDays(1).AddHours(1), Days(0));
        awayItem.Streak.Should().Be(4);

        var homeItem = MakeItem(streak: 4);
        RoutineResetService.TryReset(MakePeriod(lengthInDays: 1), homeItem, Jan5.AddDays(1).AddHours(1), Days(1));
        homeItem.Streak.Should().Be(0);
    }

    // ─── The streak walk and the freeze ──────────────────────────────────────

    private static RoutinePeriodCompletion Row(int week, int completed, bool excused = false) =>
        new()
        {
            TimePeriodId = 1,
            PeriodStart = Jan5Date.AddDays(7 * week),
            PeriodEnd = Jan5Date.AddDays(7 * week + 7),
            CompletedCount = completed,
            TotalCount = 1,
            IsExcused = excused
        };

    [Fact]
    public void RecomputeStreak_ExcusedCycleCarriesTheRun_WithoutExtendingIt()
    {
        var period = MakePeriod(streak: 0);
        period.BestStreak = 0;

        RoutineStreakFreezeService.RecomputeStreak(period, [Row(0, 1), Row(1, 1), Row(2, 0, excused: true), Row(3, 1)]);

        period.Streak.Should().Be(3);
    }

    [Fact]
    public void Apply_OnAnExcusedCycle_Refuses_AndChargesNothing()
    {
        var period = MakePeriod();
        period.FreezeBudget = 2;
        period.FreezesRemaining = 2;
        period.FreezeBudgetResetsAt = Jan5.AddMonths(1);
        var excused = Row(0, 0, excused: true);

        RoutineStreakFreezeService.Apply(period, [excused], excused, Jan5.AddDays(8))
            .Should().Be(StreakFreezeRejection.NotAMiss);

        period.FreezesRemaining.Should().Be(2);
        excused.IsFrozen.Should().BeFalse();
    }
}
