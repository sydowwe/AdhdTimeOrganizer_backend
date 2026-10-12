using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.domain.rule;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Services;

/// <summary>
/// F2-06's rule without a database: which cycles a routine has, and what one occurrence is. The reads that apply it are
/// <c>Endpoints.RoutineItemTypicalDurationTests</c>.
/// </summary>
public class RoutineItemTypicalDurationTests
{
    private const long ItemId = 7;

    private static RoutineTimePeriod MakePeriod(int lengthInDays, int anchorDay, DateTime lastResetAt) =>
        new()
        {
            UserId = 1,
            Text = "Test",
            Color = "#000000",
            LengthInDays = lengthInDays,
            ResetAnchorDay = anchorDay,
            StreakThreshold = 80,
            StreakGraceDays = 0,
            LastResetAt = lastResetAt
        };

    private static DateTime Utc(int year, int month, int day, int hour = 0) =>
        new(year, month, day, hour, 0, 0, DateTimeKind.Utc);

    private static RoutineItemLoggedTime Log(DateTime start, long seconds, long itemId = ItemId) =>
        new(itemId, start, seconds);

    /// <summary>
    /// A calendar-month period's ledger says <c>PeriodStart = PeriodEnd − 30</c>, which for February reaches back into
    /// January. The cycles are cut at the real reset instants instead, so the last days of January are January's.
    /// </summary>
    [Fact(DisplayName = "F2-06: cycles are cut at the ledger's reset instants, not at its approximate period starts")]
    public void Cycles_FollowTheRecordedResets()
    {
        var period = MakePeriod(30, 1, Utc(2026, 3, 1));
        var ledger = new List<(DateOnly, DateOnly)>
        {
            (new DateOnly(2026, 1, 30), new DateOnly(2026, 3, 1)),
            (new DateOnly(2026, 1, 2), new DateOnly(2026, 2, 1))
        };

        var cycles = RoutineItemTypicalDuration.CyclesOf(period, ledger, Utc(2026, 3, 10));

        cycles.Closed.Should().Equal(
            new RoutineItemTypicalDuration.Cycle(Utc(2026, 2, 1), Utc(2026, 3, 1)),
            new RoutineItemTypicalDuration.Cycle(Utc(2026, 1, 2), Utc(2026, 2, 1)));
        cycles.Current.Should().Be(new RoutineItemTypicalDuration.Cycle(Utc(2026, 3, 1), Utc(2026, 4, 1)));
    }

    [Fact(DisplayName = "F2-06: one sample is one occurrence — every sitting in a cycle is summed before the median")]
    public void Sample_IsTheCycleTotal()
    {
        var period = MakePeriod(7, 0, Utc(2026, 3, 22));
        var ledger = Weeks(Utc(2026, 3, 22), 3);
        var cycles = RoutineItemTypicalDuration.CyclesOf(period, ledger, Utc(2026, 3, 23));

        var typical = RoutineItemTypicalDuration.For(ItemId, cycles, isDoneThisCycle: false,
        [
            // Newest closed week: two sittings, 10 + 8 minutes.
            Log(Utc(2026, 3, 16, 9), 600), Log(Utc(2026, 3, 18, 9), 480),
            Log(Utc(2026, 3, 9, 9), 1200),
            Log(Utc(2026, 3, 2, 9), 900),
            // Another item's row in the same week is not this item's time.
            Log(Utc(2026, 3, 17, 9), 9000, itemId: ItemId + 1)
        ]);

        typical.Should().Be(new RoutineItemTypicalDuration.Typical(1080, 3),
            "the occurrences were 18, 20 and 15 minutes; a per-sitting median would say 12.5");
    }

    [Fact(DisplayName = "F2-06: the open cycle counts once the item is done in it, and not before")]
    public void OpenCycle_CountsOnlyWhenDone()
    {
        var period = MakePeriod(7, 0, Utc(2026, 3, 22));
        var cycles = RoutineItemTypicalDuration.CyclesOf(period, Weeks(Utc(2026, 3, 22), 2), Utc(2026, 3, 24));
        RoutineItemLoggedTime[] logs =
        [
            Log(Utc(2026, 3, 16, 9), 1200), Log(Utc(2026, 3, 9, 9), 1800),
            Log(Utc(2026, 3, 23, 9), 2400)
        ];

        RoutineItemTypicalDuration.For(ItemId, cycles, isDoneThisCycle: false, logs)
            .Should().BeNull("two closed occurrences, and this week's is still running");
        RoutineItemTypicalDuration.For(ItemId, cycles, isDoneThisCycle: true, logs)
            .Should().Be(new RoutineItemTypicalDuration.Typical(1800, 3));
    }

    [Fact(DisplayName = "F2-06: only the newest timed occurrences are sampled, and untimed cycles are skipped")]
    public void Samples_AreTheNewestTimedCycles()
    {
        var period = MakePeriod(1, 0, Utc(2026, 3, 30));
        var cycles = RoutineItemTypicalDuration.CyclesOf(period, Days(Utc(2026, 3, 30), 40), Utc(2026, 3, 30, 12));

        var logs = new List<RoutineItemLoggedTime>();
        // Ten timed days at one minute each, every other day back to the 11th; then ten older days at ~3 hours,
        // which would drag an uncapped median to (60 + 10 000) / 2.
        for (var i = 1; i <= RoutineItemTypicalDuration.SampleCycles; i++)
            logs.Add(Log(Utc(2026, 3, 30, 9).AddDays(-2 * i + 1), 60));
        for (var day = 21; day <= 30; day++)
            logs.Add(Log(Utc(2026, 3, 30, 9).AddDays(-day), 10_000));

        RoutineItemTypicalDuration.For(ItemId, cycles, isDoneThisCycle: false, logs)
            .Should().Be(new RoutineItemTypicalDuration.Typical(60, RoutineItemTypicalDuration.SampleCycles));
    }

    [Fact(DisplayName = "F2-06: a new period with no ledger has only its open cycle")]
    public void NoLedger_OnlyTheOpenCycle()
    {
        var period = MakePeriod(7, 0, Utc(2026, 3, 22));
        var cycles = RoutineItemTypicalDuration.CyclesOf(period, [], Utc(2026, 3, 23));

        cycles.Closed.Should().BeEmpty();
        RoutineItemTypicalDuration.For(ItemId, cycles, isDoneThisCycle: true, [Log(Utc(2026, 3, 23, 9), 600)])
            .Should().BeNull($"one occurrence is below {EstimateFitRule.RoutineItemMinimumSamples}");
    }

    /// <summary><paramref name="count"/> weekly ledger rows ending at <paramref name="lastReset"/>, newest first.</summary>
    private static List<(DateOnly, DateOnly)> Weeks(DateTime lastReset, int count) => Rows(lastReset, count, 7);

    private static List<(DateOnly, DateOnly)> Days(DateTime lastReset, int count) => Rows(lastReset, count, 1);

    private static List<(DateOnly, DateOnly)> Rows(DateTime lastReset, int count, int length)
    {
        var end = DateOnly.FromDateTime(lastReset);
        return Enumerable.Range(0, count)
            .Select(k => (end.AddDays(-length * (k + 1)), end.AddDays(-length * k)))
            .ToList();
    }
}
