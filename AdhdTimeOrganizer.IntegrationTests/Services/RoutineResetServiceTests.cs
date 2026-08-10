using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.domain.service;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Services;

public class RoutineResetServiceTests
{
    private static RoutineTimePeriod MakePeriod(
        int lengthInDays = 7,
        int anchorDay = 0,
        int streakThreshold = 80,
        int graceDays = 0,
        DateTime? lastResetAt = null)
    {
        var period = new RoutineTimePeriod
        {
            UserId = 1,
            Text = "Test",
            Color = "#000000",
            LengthInDays = lengthInDays,
            ResetAnchorDay = anchorDay,
            StreakThreshold = streakThreshold,
            StreakGraceDays = graceDays,
            LastResetAt = lastResetAt
        };
        if (lastResetAt == null)
            period.CreatedTimestamp = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return period;
    }

    private static RoutineTodoList MakeItem(bool isDone = false) =>
        new()
        {
            UserId = 1,
            TimePeriodId = 1,
            IsDone = isDone
        };

    // ─── ComputeNextReset ────────────────────────────────────────────────────

    [Fact]
    public void ComputeNextReset_NoAnchor_ReturnsEarliestDate()
    {
        var lastReset = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var period = MakePeriod(7, 0, lastResetAt: lastReset);

        var next = RoutineResetService.ComputeNextReset(period, lastReset);

        next.Date.Should().Be(new DateTime(2024, 1, 8));
    }

    [Fact]
    public void ComputeNextReset_NullLastResetAt_UsesCreatedTimestamp()
    {
        var period = MakePeriod(7, 0);
        // CreatedTimestamp = 2024-01-01 (set in MakePeriod when lastResetAt is null)

        var next = RoutineResetService.ComputeNextReset(period, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        next.Date.Should().Be(new DateTime(2024, 1, 8));
    }

    [Fact]
    public void ComputeNextReset_WeeklyAligned_SnapsToTargetWeekday()
    {
        // Last reset Tuesday 2024-01-02, anchor=1 (Monday)
        // Earliest = 2024-01-09 (Tuesday) → snap forward to Monday 2024-01-15
        var lastReset = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var period = MakePeriod(7, 1, lastResetAt: lastReset);

        var next = RoutineResetService.ComputeNextReset(period, lastReset);

        next.DayOfWeek.Should().Be(DayOfWeek.Monday);
        next.Should().BeOnOrAfter(new DateTime(2024, 1, 9, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ComputeNextReset_SundayAnchor_MappedCorrectly()
    {
        // anchor=7 maps to DayOfWeek.Sunday
        var lastReset = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc); // Monday
        var period = MakePeriod(7, 7, lastResetAt: lastReset);

        var next = RoutineResetService.ComputeNextReset(period, lastReset);

        next.DayOfWeek.Should().Be(DayOfWeek.Sunday);
    }

    [Fact]
    public void ComputeNextReset_BiweeklyAligned_SnapsToTargetWeekday()
    {
        // 14 days is weekly aligned (14 % 7 == 0), anchor=5 (Friday)
        // Last Mon 2024-01-01, earliest = 2024-01-15 (Mon) → snap to Fri 2024-01-19
        var lastReset = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var period = MakePeriod(14, 5, lastResetAt: lastReset);

        var next = RoutineResetService.ComputeNextReset(period, lastReset);

        next.DayOfWeek.Should().Be(DayOfWeek.Friday);
        next.Should().BeOnOrAfter(new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ComputeNextReset_MonthlyPeriod_SnapsToTargetDayOfNextMonth()
    {
        // LengthInDays=30, anchor=15, last reset 2024-01-20 → next 2024-02-15
        var lastReset = new DateTime(2024, 1, 20, 0, 0, 0, DateTimeKind.Utc);
        var period = MakePeriod(30, 15, lastResetAt: lastReset);

        var next = RoutineResetService.ComputeNextReset(period, lastReset);

        next.Year.Should().Be(2024);
        next.Month.Should().Be(2);
        next.Day.Should().Be(15);
    }

    [Fact]
    public void ComputeNextReset_YearlyPeriod_SnapsToSameDayNextYear()
    {
        // LengthInDays=365, anchor=10, last reset 2024-03-01 → next 2025-03-10
        var lastReset = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var period = MakePeriod(365, 10, lastResetAt: lastReset);

        var next = RoutineResetService.ComputeNextReset(period, lastReset);

        next.Year.Should().Be(2025);
        next.Month.Should().Be(3);
        next.Day.Should().Be(10);
    }

    [Fact]
    public void ComputeNextReset_NonWeeklyNonMonthly_UsesNextOccurrenceOfDayInMonth()
    {
        // LengthInDays=10, anchor=20, last reset 2024-01-05 → earliest 2024-01-15 (day<20) → 2024-01-20
        var lastReset = new DateTime(2024, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var period = MakePeriod(10, 20, lastResetAt: lastReset);

        var next = RoutineResetService.ComputeNextReset(period, lastReset);

        next.Day.Should().Be(20);
        next.Month.Should().Be(1);
    }

    // ─── CheckGrace ──────────────────────────────────────────────────────────

    [Fact]
    public void CheckGrace_NoGrace_ReturnsFalse()
    {
        var period = MakePeriod();
        period.Streak = 5;

        var changed = RoutineResetService.CheckGrace(period, DateTime.UtcNow);

        changed.Should().BeFalse();
        period.Streak.Should().Be(5);
    }

    [Fact]
    public void CheckGrace_GraceStillActive_ReturnsFalse()
    {
        var period = MakePeriod();
        period.Streak = 5;
        period.StreakGraceUntil = DateTime.UtcNow.AddDays(2);

        var changed = RoutineResetService.CheckGrace(period, DateTime.UtcNow);

        changed.Should().BeFalse();
        period.Streak.Should().Be(5);
    }

    [Fact]
    public void CheckGrace_GraceExpired_ResetsStreakAndReturnsTrue()
    {
        var period = MakePeriod();
        period.Streak = 5;
        period.StreakGraceUntil = DateTime.UtcNow.AddDays(-1);

        var changed = RoutineResetService.CheckGrace(period, DateTime.UtcNow);

        changed.Should().BeTrue();
        period.Streak.Should().Be(0);
        period.StreakGraceUntil.Should().BeNull();
    }

    // ─── TryReset (single item) ──────────────────────────────────────────────

    [Fact]
    public void TryReset_Single_BeforeResetTime_ReturnsFalse()
    {
        var period = MakePeriod(7, lastResetAt: DateTime.UtcNow);
        var item = MakeItem(true);

        var result = RoutineResetService.TryReset(period, item, DateTime.UtcNow);

        result.Should().BeFalse();
        item.IsDone.Should().BeTrue();
    }

    [Fact]
    public void TryReset_Single_AfterResetTime_ResetsItemAndUpdatesPeriod()
    {
        var period = MakePeriod(7, lastResetAt: DateTime.UtcNow.AddDays(-8));
        var item = MakeItem(true);
        // DoneCount is step-counted: SetDone only rewrites it when TotalCount is set, so a
        // DoneCount without a TotalCount is not a state the app can produce.
        item.TotalCount = 3;
        item.DoneCount = 3;

        var result = RoutineResetService.TryReset(period, item, DateTime.UtcNow);

        result.Should().BeTrue();
        item.IsDone.Should().BeFalse();
        item.DoneCount.Should().Be(0);
        period.LastResetAt.Should().NotBeNull();
    }

    [Fact]
    public void TryReset_Single_ClearsAllSteps()
    {
        var period = MakePeriod(7, lastResetAt: DateTime.UtcNow.AddDays(-8));
        var item = MakeItem();
        item.Steps =
        [
            new TodoListStep { Name = "Step1", IsDone = true },
            new TodoListStep { Name = "Step2", IsDone = true }
        ];

        RoutineResetService.TryReset(period, item, DateTime.UtcNow);

        item.Steps.Should().AllSatisfy(s => s.IsDone.Should().BeFalse());
    }

    [Fact]
    public void TryReset_Single_SetsLastResetDateToToday()
    {
        var period = MakePeriod(7, lastResetAt: DateTime.UtcNow.AddDays(-8));
        var item = MakeItem();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        RoutineResetService.TryReset(period, item, DateTime.UtcNow);

        item.LastResetDate.Should().Be(today);
    }

    // ─── TryReset (batch items) ──────────────────────────────────────────────

    [Fact]
    public void TryReset_Batch_BeforeResetTime_ReturnsNull()
    {
        var period = MakePeriod(7, lastResetAt: DateTime.UtcNow);

        var result = RoutineResetService.TryReset(period, [MakeItem()], DateTime.UtcNow);

        result.Should().BeNull();
    }

    [Fact]
    public void TryReset_Batch_AllItemsDone_IncrementsPeriodStreak()
    {
        var period = MakePeriod(7, streakThreshold: 80, lastResetAt: DateTime.UtcNow.AddDays(-8));
        period.Streak = 2;
        var items = new List<RoutineTodoList> { MakeItem(true), MakeItem(true) };

        RoutineResetService.TryReset(period, items, DateTime.UtcNow);

        period.Streak.Should().Be(3);
        period.BestStreak.Should().Be(3);
    }

    [Fact]
    public void TryReset_Batch_BelowThreshold_NoGrace_ResetsStreak()
    {
        var period = MakePeriod(7, streakThreshold: 80, graceDays: 0, lastResetAt: DateTime.UtcNow.AddDays(-8));
        period.Streak = 5;
        var items = new List<RoutineTodoList> { MakeItem(false), MakeItem(false) };

        RoutineResetService.TryReset(period, items, DateTime.UtcNow);

        period.Streak.Should().Be(0);
        period.StreakGraceUntil.Should().BeNull();
    }

    [Fact]
    public void TryReset_Batch_BelowThreshold_WithGrace_SetsGraceUntilAndPreservesStreak()
    {
        var period = MakePeriod(7, streakThreshold: 80, graceDays: 3, lastResetAt: DateTime.UtcNow.AddDays(-8));
        period.Streak = 5;
        var items = new List<RoutineTodoList> { MakeItem(false), MakeItem(false) };

        RoutineResetService.TryReset(period, items, DateTime.UtcNow);

        period.Streak.Should().Be(5);
        period.StreakGraceUntil.Should().NotBeNull();
        period.StreakGraceUntil.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public void TryReset_Batch_EmptyItems_ReturnsCompletionAndSkipsStreakLogic()
    {
        var period = MakePeriod(7, lastResetAt: DateTime.UtcNow.AddDays(-8));
        period.Streak = 3;

        var result = RoutineResetService.TryReset(period, new List<RoutineTodoList>(), DateTime.UtcNow);

        result.Should().NotBeNull();
        result!.Value.Completion.TotalCount.Should().Be(0);
        result.Value.Outcome.Should().Be(StreakOutcome.NotEvaluated);
        period.Streak.Should().Be(3);
    }

    [Fact]
    public void TryReset_Batch_CompletionRecord_HasCorrectCounts()
    {
        var period = MakePeriod(7, streakThreshold: 50, lastResetAt: DateTime.UtcNow.AddDays(-8));
        var items = new List<RoutineTodoList>
        {
            MakeItem(true),
            MakeItem(true),
            MakeItem(false)
        };

        var result = RoutineResetService.TryReset(period, items, DateTime.UtcNow);

        result.Should().NotBeNull();
        result!.Value.Completion.CompletedCount.Should().Be(2);
        result.Value.Completion.TotalCount.Should().Be(3);
    }

    [Fact]
    public void TryReset_Batch_ResetsAllItemsAfterPeriodEnd()
    {
        var period = MakePeriod(7, lastResetAt: DateTime.UtcNow.AddDays(-8));
        var items = new List<RoutineTodoList>
        {
            MakeItem(true),
            MakeItem(true)
        };
        // Both items need a TotalCount for DoneCount to be meaningful — see SetDone.
        items[0].TotalCount = 5;
        items[0].DoneCount = 5;
        items[1].TotalCount = 2;
        items[1].DoneCount = 2;

        RoutineResetService.TryReset(period, items, DateTime.UtcNow);

        items.Should().AllSatisfy(i =>
        {
            i.IsDone.Should().BeFalse();
            i.DoneCount.Should().Be(0);
        });
    }

    // ─── TryReset streak outcome ─────────────────────────────────────────────
    // The outcome is what the end-of-period notification says about the streak, so a wrong one is a wrong
    // message to the user rather than a wrong row. Each branch of the streak evaluation is pinned.

    [Theory]
    [InlineData(true, 0, StreakOutcome.Extended)]
    [InlineData(false, 3, StreakOutcome.OnGrace)]
    [InlineData(false, 0, StreakOutcome.Broken)]
    public void TryReset_Batch_ReportsStreakOutcome(bool itemsDone, int graceDays, StreakOutcome expected)
    {
        var period = MakePeriod(7, streakThreshold: 80, graceDays: graceDays, lastResetAt: DateTime.UtcNow.AddDays(-8));
        period.Streak = 4;

        var result = RoutineResetService.TryReset(period, [MakeItem(itemsDone), MakeItem(itemsDone)], DateTime.UtcNow);

        result.Should().NotBeNull();
        result!.Value.Outcome.Should().Be(expected);
    }

    /// <summary>
    /// The mark is scoped to one cycle. Without this the next cycle's nudge would be compared against a mark
    /// left over from the previous one and — for a rolling period, where the recomputed instant can coincide —
    /// could be suppressed entirely.
    /// </summary>
    [Fact]
    public void TryReset_Batch_ClearsTheEndingSoonMark()
    {
        var period = MakePeriod(7, lastResetAt: DateTime.UtcNow.AddDays(-8));
        period.EndingSoonNotifiedFor = DateTime.UtcNow.AddDays(-1);

        RoutineResetService.TryReset(period, [MakeItem()], DateTime.UtcNow);

        period.EndingSoonNotifiedFor.Should().BeNull();
    }

    // ─── EvaluateEndingSoon ──────────────────────────────────────────────────

    [Fact]
    public void EvaluateEndingSoon_NoLeadConfigured_ReturnsNull()
    {
        // ReminderLeadDays is null by default — opting in is what turns the nudge on.
        var period = MakePeriod(7, lastResetAt: DateTime.UtcNow.AddDays(-6));

        RoutineResetService.EvaluateEndingSoon(period, DateTime.UtcNow).Should().BeNull();
    }

    [Fact]
    public void EvaluateEndingSoon_OutsideWindow_ReturnsNull()
    {
        // Reset is 6 days out, lead is 2 — the window has not opened yet.
        var period = MakePeriod(7, lastResetAt: DateTime.UtcNow.AddDays(-1));
        period.ReminderLeadDays = 2;

        RoutineResetService.EvaluateEndingSoon(period, DateTime.UtcNow).Should().BeNull();
    }

    [Fact]
    public void EvaluateEndingSoon_InsideWindow_ReturnsNudgeWithDaysLeft()
    {
        var now = new DateTime(2024, 1, 6, 12, 0, 0, DateTimeKind.Utc);
        // Rolling 7-day period last reset on the 1st → next reset 2024-01-08 00:00, i.e. 1.5 days out.
        var period = MakePeriod(7, lastResetAt: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        period.ReminderLeadDays = 3;

        var nudge = RoutineResetService.EvaluateEndingSoon(period, now);

        nudge.Should().NotBeNull();
        nudge!.Value.NextReset.Should().Be(new DateTime(2024, 1, 8, 0, 0, 0, DateTimeKind.Utc));
        // Ceiling, so a reset a day and a half out never reads as "0 days left".
        nudge.Value.DaysLeft.Should().Be(2);
    }

    [Fact]
    public void EvaluateEndingSoon_AlreadyNotifiedForThisReset_ReturnsNull()
    {
        var now = new DateTime(2024, 1, 6, 12, 0, 0, DateTimeKind.Utc);
        var period = MakePeriod(7, lastResetAt: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        period.ReminderLeadDays = 3;
        period.EndingSoonNotifiedFor = RoutineResetService.ComputeNextReset(period, now);

        RoutineResetService.EvaluateEndingSoon(period, now).Should().BeNull();
    }

    /// <summary>
    /// Past the reset the period belongs to the reset path, and a "2 days left" nudge racing the reset that
    /// clears the items would be worse than saying nothing.
    /// </summary>
    [Fact]
    public void EvaluateEndingSoon_PastTheReset_ReturnsNull()
    {
        var period = MakePeriod(7, lastResetAt: DateTime.UtcNow.AddDays(-8));
        period.ReminderLeadDays = 3;

        RoutineResetService.EvaluateEndingSoon(period, DateTime.UtcNow).Should().BeNull();
    }

    // ─── ShouldWarnGraceExpiring ─────────────────────────────────────────────

    [Fact]
    public void ShouldWarnGraceExpiring_NoGraceWindow_ReturnsFalse()
    {
        RoutineResetService.ShouldWarnGraceExpiring(MakePeriod(), DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void ShouldWarnGraceExpiring_GraceStillFarOff_ReturnsFalse()
    {
        var period = MakePeriod();
        period.StreakGraceUntil = DateTime.UtcNow.AddDays(3);

        RoutineResetService.ShouldWarnGraceExpiring(period, DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void ShouldWarnGraceExpiring_WithinLead_ReturnsTrue()
    {
        var period = MakePeriod();
        period.StreakGraceUntil = DateTime.UtcNow.AddHours(12);

        RoutineResetService.ShouldWarnGraceExpiring(period, DateTime.UtcNow).Should().BeTrue();
    }

    /// <summary>Once the window has lapsed there is nothing to warn about — CheckGrace has already broken it.</summary>
    [Fact]
    public void ShouldWarnGraceExpiring_AlreadyLapsed_ReturnsFalse()
    {
        var period = MakePeriod();
        period.StreakGraceUntil = DateTime.UtcNow.AddHours(-1);

        RoutineResetService.ShouldWarnGraceExpiring(period, DateTime.UtcNow).Should().BeFalse();
    }

    /// <summary>Keyed on the grace instant, so a second failed period opening a new window warns again.</summary>
    [Fact]
    public void ShouldWarnGraceExpiring_MarkAppliesOnlyToTheWindowItWasSetFor()
    {
        var period = MakePeriod();
        period.StreakGraceUntil = DateTime.UtcNow.AddHours(12);
        period.GraceNotifiedFor = period.StreakGraceUntil;

        RoutineResetService.ShouldWarnGraceExpiring(period, DateTime.UtcNow).Should().BeFalse();

        period.StreakGraceUntil = DateTime.UtcNow.AddHours(18);

        RoutineResetService.ShouldWarnGraceExpiring(period, DateTime.UtcNow).Should().BeTrue();
    }

    // ─── UpdateItemStreak ────────────────────────────────────────────────────

    [Fact]
    public void UpdateItemStreak_ItemNotDone_NoChange()
    {
        var item = MakeItem(false);
        item.Streak = 3;
        var before = item.LastCompletedAt;

        RoutineResetService.UpdateItemStreak(item, DateTime.UtcNow);

        item.Streak.Should().Be(3);
        item.LastCompletedAt.Should().Be(before);
    }

    [Fact]
    public void UpdateItemStreak_ItemDone_IncrementsStreak()
    {
        var item = MakeItem(true);
        item.Streak = 2;
        var now = DateTime.UtcNow;

        RoutineResetService.UpdateItemStreak(item, now);

        item.Streak.Should().Be(3);
        item.LastCompletedAt.Should().Be(now);
    }

    [Fact]
    public void UpdateItemStreak_ItemDone_UpdatesBestStreakWhenExceeded()
    {
        var item = MakeItem(true);
        item.Streak = 5;
        item.BestStreak = 5;

        RoutineResetService.UpdateItemStreak(item, DateTime.UtcNow);

        item.BestStreak.Should().Be(6);
    }

    [Fact]
    public void UpdateItemStreak_ItemDone_PreservesBestStreakWhenNotExceeded()
    {
        var item = MakeItem(true);
        item.Streak = 2;
        item.BestStreak = 10;

        RoutineResetService.UpdateItemStreak(item, DateTime.UtcNow);

        item.BestStreak.Should().Be(10);
    }
}