using System.Globalization;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.service;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Services;

/// <summary>
/// The plan-vs-actual arithmetic, exercised without a database. Everything here is a pure function over rows,
/// and every case below names a decision someone could plausibly reverse by accident later — most of all the
/// two different populations the two sums cover.
/// <para>
/// The other half — which rows are eligible in the first place, and that the aggregate reaches the client —
/// lives in the query and is pinned in <c>Endpoints.PlanVsActualTrendTests</c>.
/// </para>
/// </summary>
public class PlanVsActualTrendServiceTests
{
    private static PlanVsActualTrendService.PlanVsActualTaskRow Row(
        string plannedStart,
        string plannedEnd,
        PlannerTaskStatus status = PlannerTaskStatus.Completed,
        string? actualStart = null,
        string? actualEnd = null) =>
        new(Time(plannedStart), Time(plannedEnd), status, TimeOrNull(actualStart), TimeOrNull(actualEnd));

    private static TimeOnly Time(string value) => TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);

    private static TimeOnly? TimeOrNull(string? value) => value is null ? null : Time(value);

    private static PlanVsActualTrendService.PlanVsActualTrend Aggregate(
        params PlanVsActualTrendService.PlanVsActualTaskRow[] rows) =>
        PlanVsActualTrendService.Aggregate(rows);

    [Fact]
    public void Aggregate_NoRows_IsAllZeroes()
    {
        Aggregate().Should().Be(new PlanVsActualTrendService.PlanVsActualTrend(0, 0, 0, 0, 0, 0));
    }

    /// <summary>The headline case: an hour planned, ninety minutes logged, started late and ran long.</summary>
    [Fact]
    public void Aggregate_TaskThatStartedLateAndRanLong_CountsInBoth()
    {
        var trend = Aggregate(Row("09:00", "10:00", actualStart: "09:15", actualEnd: "10:45"));

        trend.TaskCount.Should().Be(1);
        trend.PlannedMinutes.Should().Be(60);
        trend.ActualMinutes.Should().Be(90);
        trend.StartedLateCount.Should().Be(1);
        trend.RanLongerCount.Should().Be(1);
        trend.NeverHappenedCount.Should().Be(0);
    }

    /// <summary>
    /// Late and long are independent questions. A task started an hour early and still run over is long but
    /// not late — counting them together would let one number stand in for the other and hide the shape of
    /// the drift the trend exists to describe.
    /// </summary>
    [Fact]
    public void Aggregate_StartedEarlyButRanLong_IsLongButNotLate()
    {
        var trend = Aggregate(Row("09:00", "10:00", actualStart: "08:00", actualEnd: "09:30"));

        trend.StartedLateCount.Should().Be(0);
        trend.RanLongerCount.Should().Be(1);
    }

    /// <summary>Starting exactly on time is not late, and finishing to the minute is not longer.</summary>
    [Fact]
    public void Aggregate_TaskRunExactlyAsPlanned_IsNeitherLateNorLong()
    {
        var trend = Aggregate(Row("09:00", "10:00", actualStart: "09:00", actualEnd: "10:00"));

        trend.PlannedMinutes.Should().Be(60);
        trend.ActualMinutes.Should().Be(60);
        trend.StartedLateCount.Should().Be(0);
        trend.RanLongerCount.Should().Be(0);
    }

    /// <summary>
    /// <b>The two sums cover different populations.</b> A task that never happened keeps its planned minutes —
    /// it was a real commitment — but contributes no actual minutes and lands in
    /// <c>NeverHappenedCount</c>. Dropping its plan instead would make an abandoned range read as perfectly
    /// estimated, which is the exact opposite of the truth.
    /// </summary>
    [Theory]
    [InlineData(PlannerTaskStatus.NotStarted)]
    [InlineData(PlannerTaskStatus.InProgress)]
    [InlineData(PlannerTaskStatus.OnHold)]
    public void Aggregate_TaskThatNeverCompleted_KeepsItsPlanButAddsNoActual(PlannerTaskStatus status)
    {
        var trend = Aggregate(Row("09:00", "10:00", status));

        trend.TaskCount.Should().Be(1);
        trend.PlannedMinutes.Should().Be(60);
        trend.ActualMinutes.Should().Be(0);
        trend.NeverHappenedCount.Should().Be(1);
        trend.StartedLateCount.Should().Be(0);
        trend.RanLongerCount.Should().Be(0);
    }

    /// <summary>
    /// An in-progress task carrying a start time is still a task that never happened — it is <i>not</i>
    /// measurable, and half a measurement must not reach <c>ActualMinutes</c>.
    /// </summary>
    [Fact]
    public void Aggregate_InProgressTaskWithAStartTime_IsStillNeverHappened()
    {
        var trend = Aggregate(Row("09:00", "10:00", PlannerTaskStatus.InProgress, actualStart: "09:05"));

        trend.NeverHappenedCount.Should().Be(1);
        trend.ActualMinutes.Should().Be(0);
        trend.StartedLateCount.Should().Be(0);
    }

    /// <summary>
    /// A completed task missing either actual time is <b>unmeasurable, not missed</b>: it counts in
    /// <c>TaskCount</c> and <c>PlannedMinutes</c>, contributes nothing to the actual sum, and is not a
    /// "never happened". Inferring the missing end from the plan would fabricate exactly the drift — zero —
    /// that the number exists to measure.
    /// </summary>
    [Theory]
    [InlineData("09:05", null)]
    [InlineData(null, "10:05")]
    [InlineData(null, null)]
    public void Aggregate_CompletedTaskMissingAnActualTime_IsCountedButNotMeasured(string? start, string? end)
    {
        var trend = Aggregate(Row("09:00", "10:00", actualStart: start, actualEnd: end));

        trend.TaskCount.Should().Be(1);
        trend.PlannedMinutes.Should().Be(60);
        trend.ActualMinutes.Should().Be(0);
        trend.NeverHappenedCount.Should().Be(0);
        trend.StartedLateCount.Should().Be(0);
        trend.RanLongerCount.Should().Be(0);
    }

    /// <summary>
    /// Late and long are counted over the <i>measured</i> tasks only. Three tasks, one measured: the counters
    /// can never exceed the population that carries actual times, however many tasks the range holds.
    /// </summary>
    [Fact]
    public void Aggregate_CountersNeverExceedTheMeasuredPopulation()
    {
        var trend = Aggregate(
            Row("09:00", "10:00", actualStart: "09:30", actualEnd: "11:00"),
            Row("11:00", "12:00", PlannerTaskStatus.NotStarted),
            Row("13:00", "14:00"));

        trend.TaskCount.Should().Be(3);
        trend.PlannedMinutes.Should().Be(180);
        trend.ActualMinutes.Should().Be(90);
        trend.StartedLateCount.Should().Be(1);
        trend.RanLongerCount.Should().Be(1);
        trend.NeverHappenedCount.Should().Be(1);
    }

    // ─── midnight ────────────────────────────────────────────────────────────

    /// <summary>
    /// A task planned across midnight is ninety minutes, not a negative span — the single most damaging way
    /// this could be wrong, because one such task would drag a whole range's planned total below zero.
    /// </summary>
    [Fact]
    public void Aggregate_TaskPlannedAcrossMidnight_HasAPositiveSpan()
    {
        var trend = Aggregate(Row("23:00", "00:30", actualStart: "23:00", actualEnd: "01:00"));

        trend.PlannedMinutes.Should().Be(90);
        trend.ActualMinutes.Should().Be(120);
        trend.RanLongerCount.Should().Be(1);
    }

    /// <summary>
    /// Times of day carry no date, so a task planned for 23:50 and started at 00:10 is twenty minutes late —
    /// not twenty-three hours and forty minutes early. Read naively it would count as the day's most punctual
    /// task.
    /// </summary>
    [Fact]
    public void Aggregate_StartedJustAfterMidnight_IsLateNotWildlyEarly()
    {
        var trend = Aggregate(Row("23:50", "00:50", actualStart: "00:10", actualEnd: "01:10"));

        trend.StartedLateCount.Should().Be(1);
    }

    /// <summary>And the mirror image: planned for 00:10, started at 23:50, is twenty minutes early.</summary>
    [Fact]
    public void Aggregate_StartedJustBeforeMidnight_IsEarlyNotWildlyLate()
    {
        var trend = Aggregate(Row("00:10", "01:10", actualStart: "23:50", actualEnd: "00:50"));

        trend.StartedLateCount.Should().Be(0);
    }

    /// <summary>
    /// A degenerate span reads as zero rather than a full day. Both readings are defensible; this is the one
    /// that cannot silently add 24 hours to a range's planned total.
    /// </summary>
    [Fact]
    public void Aggregate_TaskWhoseEndEqualsItsStart_IsZeroMinutesNotADay()
    {
        Aggregate(Row("09:00", "09:00", actualStart: "09:00", actualEnd: "09:00"))
            .Should().Be(new PlanVsActualTrendService.PlanVsActualTrend(1, 0, 0, 0, 0, 0));
    }
}
