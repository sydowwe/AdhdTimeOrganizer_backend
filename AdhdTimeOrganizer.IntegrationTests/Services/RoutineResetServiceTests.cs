using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.domain.service;
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
        new RoutineTodoList
        {
            UserId = 1,
            TimePeriodId = 1,
            IsDone = isDone
        };

    // ─── ComputeNextReset ────────────────────────────────────────────────────

    [Fact]
    public void ComputeNextReset_NoAnchor_ReturnsEarliestDate()
    {
        var period = MakePeriod(7, anchorDay: 0,
            lastResetAt: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var next = RoutineResetService.ComputeNextReset(period);

        next.Date.Should().Be(new DateTime(2024, 1, 8));
    }

    [Fact]
    public void ComputeNextReset_NullLastResetAt_UsesCreatedTimestamp()
    {
        var period = MakePeriod(7, anchorDay: 0);
        // CreatedTimestamp = 2024-01-01 (set in MakePeriod when lastResetAt is null)

        var next = RoutineResetService.ComputeNextReset(period);

        next.Date.Should().Be(new DateTime(2024, 1, 8));
    }

    [Fact]
    public void ComputeNextReset_WeeklyAligned_SnapsToTargetWeekday()
    {
        // Last reset Tuesday 2024-01-02, anchor=1 (Monday)
        // Earliest = 2024-01-09 (Tuesday) → snap forward to Monday 2024-01-15
        var period = MakePeriod(7, anchorDay: 1,
            lastResetAt: new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        var next = RoutineResetService.ComputeNextReset(period);

        next.DayOfWeek.Should().Be(DayOfWeek.Monday);
        next.Should().BeOnOrAfter(new DateTime(2024, 1, 9, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ComputeNextReset_SundayAnchor_MappedCorrectly()
    {
        // anchor=7 maps to DayOfWeek.Sunday
        var period = MakePeriod(7, anchorDay: 7,
            lastResetAt: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)); // Monday

        var next = RoutineResetService.ComputeNextReset(period);

        next.DayOfWeek.Should().Be(DayOfWeek.Sunday);
    }

    [Fact]
    public void ComputeNextReset_BiweeklyAligned_SnapsToTargetWeekday()
    {
        // 14 days is weekly aligned (14 % 7 == 0), anchor=5 (Friday)
        // Last Mon 2024-01-01, earliest = 2024-01-15 (Mon) → snap to Fri 2024-01-19
        var period = MakePeriod(14, anchorDay: 5,
            lastResetAt: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var next = RoutineResetService.ComputeNextReset(period);

        next.DayOfWeek.Should().Be(DayOfWeek.Friday);
        next.Should().BeOnOrAfter(new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ComputeNextReset_MonthlyPeriod_SnapsToTargetDayOfNextMonth()
    {
        // LengthInDays=30, anchor=15, last reset 2024-01-20 → next 2024-02-15
        var period = MakePeriod(30, anchorDay: 15,
            lastResetAt: new DateTime(2024, 1, 20, 0, 0, 0, DateTimeKind.Utc));

        var next = RoutineResetService.ComputeNextReset(period);

        next.Year.Should().Be(2024);
        next.Month.Should().Be(2);
        next.Day.Should().Be(15);
    }

    [Fact]
    public void ComputeNextReset_YearlyPeriod_SnapsToSameDayNextYear()
    {
        // LengthInDays=365, anchor=10, last reset 2024-03-01 → next 2025-03-10
        var period = MakePeriod(365, anchorDay: 10,
            lastResetAt: new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc));

        var next = RoutineResetService.ComputeNextReset(period);

        next.Year.Should().Be(2025);
        next.Month.Should().Be(3);
        next.Day.Should().Be(10);
    }

    [Fact]
    public void ComputeNextReset_NonWeeklyNonMonthly_UsesNextOccurrenceOfDayInMonth()
    {
        // LengthInDays=10, anchor=20, last reset 2024-01-05 → earliest 2024-01-15 (day<20) → 2024-01-20
        var period = MakePeriod(10, anchorDay: 20,
            lastResetAt: new DateTime(2024, 1, 5, 0, 0, 0, DateTimeKind.Utc));

        var next = RoutineResetService.ComputeNextReset(period);

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
        var item = MakeItem(isDone: true);

        var result = RoutineResetService.TryReset(period, item, DateTime.UtcNow);

        result.Should().BeFalse();
        item.IsDone.Should().BeTrue();
    }

    [Fact]
    public void TryReset_Single_AfterResetTime_ResetsItemAndUpdatesPeriod()
    {
        var period = MakePeriod(7, lastResetAt: DateTime.UtcNow.AddDays(-8));
        var item = MakeItem(isDone: true);
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
        item.Steps = [
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
        var items = new List<RoutineTodoList> { MakeItem(isDone: true), MakeItem(isDone: true) };

        RoutineResetService.TryReset(period, items, DateTime.UtcNow);

        period.Streak.Should().Be(3);
        period.BestStreak.Should().Be(3);
    }

    [Fact]
    public void TryReset_Batch_BelowThreshold_NoGrace_ResetsStreak()
    {
        var period = MakePeriod(7, streakThreshold: 80, graceDays: 0, lastResetAt: DateTime.UtcNow.AddDays(-8));
        period.Streak = 5;
        var items = new List<RoutineTodoList> { MakeItem(isDone: false), MakeItem(isDone: false) };

        RoutineResetService.TryReset(period, items, DateTime.UtcNow);

        period.Streak.Should().Be(0);
        period.StreakGraceUntil.Should().BeNull();
    }

    [Fact]
    public void TryReset_Batch_BelowThreshold_WithGrace_SetsGraceUntilAndPreservesStreak()
    {
        var period = MakePeriod(7, streakThreshold: 80, graceDays: 3, lastResetAt: DateTime.UtcNow.AddDays(-8));
        period.Streak = 5;
        var items = new List<RoutineTodoList> { MakeItem(isDone: false), MakeItem(isDone: false) };

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
        result!.TotalCount.Should().Be(0);
        period.Streak.Should().Be(3);
    }

    [Fact]
    public void TryReset_Batch_CompletionRecord_HasCorrectCounts()
    {
        var period = MakePeriod(7, streakThreshold: 50, lastResetAt: DateTime.UtcNow.AddDays(-8));
        var items = new List<RoutineTodoList>
        {
            MakeItem(isDone: true),
            MakeItem(isDone: true),
            MakeItem(isDone: false)
        };

        var completion = RoutineResetService.TryReset(period, items, DateTime.UtcNow);

        completion.Should().NotBeNull();
        completion!.CompletedCount.Should().Be(2);
        completion.TotalCount.Should().Be(3);
    }

    [Fact]
    public void TryReset_Batch_ResetsAllItemsAfterPeriodEnd()
    {
        var period = MakePeriod(7, lastResetAt: DateTime.UtcNow.AddDays(-8));
        var items = new List<RoutineTodoList>
        {
            MakeItem(isDone: true),
            MakeItem(isDone: true)
        };
        items[0].DoneCount = 5;

        RoutineResetService.TryReset(period, items, DateTime.UtcNow);

        items.Should().AllSatisfy(i =>
        {
            i.IsDone.Should().BeFalse();
            i.DoneCount.Should().Be(0);
        });
    }

    // ─── UpdateItemStreak ────────────────────────────────────────────────────

    [Fact]
    public void UpdateItemStreak_ItemNotDone_NoChange()
    {
        var item = MakeItem(isDone: false);
        item.Streak = 3;
        var before = item.LastCompletedAt;

        RoutineResetService.UpdateItemStreak(item, DateTime.UtcNow);

        item.Streak.Should().Be(3);
        item.LastCompletedAt.Should().Be(before);
    }

    [Fact]
    public void UpdateItemStreak_ItemDone_IncrementsStreak()
    {
        var item = MakeItem(isDone: true);
        item.Streak = 2;
        var now = DateTime.UtcNow;

        RoutineResetService.UpdateItemStreak(item, now);

        item.Streak.Should().Be(3);
        item.LastCompletedAt.Should().Be(now);
    }

    [Fact]
    public void UpdateItemStreak_ItemDone_UpdatesBestStreakWhenExceeded()
    {
        var item = MakeItem(isDone: true);
        item.Streak = 5;
        item.BestStreak = 5;

        RoutineResetService.UpdateItemStreak(item, DateTime.UtcNow);

        item.BestStreak.Should().Be(6);
    }

    [Fact]
    public void UpdateItemStreak_ItemDone_PreservesBestStreakWhenNotExceeded()
    {
        var item = MakeItem(isDone: true);
        item.Streak = 2;
        item.BestStreak = 10;

        RoutineResetService.UpdateItemStreak(item, DateTime.UtcNow);

        item.BestStreak.Should().Be(10);
    }
}
