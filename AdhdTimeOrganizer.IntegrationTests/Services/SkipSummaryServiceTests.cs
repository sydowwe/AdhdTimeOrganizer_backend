using System.Globalization;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.service;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Services;

/// <summary>
/// The skip-summary grouping, exercised without a database. Which rows are eligible — background, today, other
/// users — lives in the query and is pinned in <c>Endpoints.SkipSummaryTests</c>.
/// </summary>
public class SkipSummaryServiceTests
{
    private static SkipSummaryService.SkipSummaryTaskRow Row(
        string plannedStart,
        PlannerTaskStatus status = PlannerTaskStatus.NotStarted,
        string? reason = null,
        SkipReasonKey? key = null) =>
        new(TimeOnly.ParseExact(plannedStart, "HH:mm", CultureInfo.InvariantCulture), status, reason, key);

    private static SkipSummaryService.SkipSummaryTaskRow Skip(string plannedStart, string? reason = null) =>
        Row(plannedStart, PlannerTaskStatus.Cancelled, reason);

    private static SkipSummaryService.SkipSummary Aggregate(params SkipSummaryService.SkipSummaryTaskRow[] rows) =>
        SkipSummaryService.Aggregate(rows);

    [Fact]
    public void Aggregate_NoRows_IsEmpty()
    {
        var summary = Aggregate();

        summary.SkippedCount.Should().Be(0);
        summary.Reasons.Should().BeEmpty();
        summary.ByStartHour.Should().BeEmpty();
    }

    /// <summary>
    /// Exact stored value, ordinal. "ran out of time" is not "Ran out of time" here — the client's fold merges
    /// them, and it must be the only place that does, so the single-day and range lines agree.
    /// </summary>
    [Fact]
    public void Aggregate_GroupsReasonsByExactValue_WithoutFolding()
    {
        var summary = Aggregate(
            Skip("09:00", "Meeting ran long"),
            Skip("10:00", "Meeting ran long"),
            Skip("11:00", "meeting ran long"),
            Skip("12:00", "Stretilo sa to s obedom"));

        summary.Reasons.Should().BeEquivalentTo(new[]
        {
            new SkipSummaryService.ReasonCount("Meeting ran long", null, 2),
            new SkipSummaryService.ReasonCount("meeting ran long", null, 1),
            new SkipSummaryService.ReasonCount("Stretilo sa to s obedom", null, 1)
        });
    }

    /// <summary>
    /// A built-in key is its own group, apart from any free text — and a free text that merely resembles the
    /// label stays text. Both locales of one key are one group, which is the point of storing keys.
    /// </summary>
    [Fact]
    public void Aggregate_GroupsKeysByKey_ApartFromText()
    {
        var summary = Aggregate(
            Row("09:00", PlannerTaskStatus.Cancelled, null, SkipReasonKey.NoTime),
            Row("10:00", PlannerTaskStatus.Cancelled, null, SkipReasonKey.NoTime),
            Row("11:00", PlannerTaskStatus.Cancelled, null, SkipReasonKey.NoEnergy),
            Skip("12:00", "ran out of time again"));

        summary.SkippedCount.Should().Be(4);
        summary.Reasons.Should().BeEquivalentTo(new[]
        {
            new SkipSummaryService.ReasonCount(null, SkipReasonKey.NoTime, 2),
            new SkipSummaryService.ReasonCount(null, SkipReasonKey.NoEnergy, 1),
            new SkipSummaryService.ReasonCount("ran out of time again", null, 1)
        });
    }

    /// <summary>A skip with no reason is counted, as its own null group, so the reason counts sum to the total.</summary>
    [Fact]
    public void Aggregate_SkipWithoutReason_IsItsOwnNullGroup_AndTotalsAddUp()
    {
        var summary = Aggregate(
            Skip("09:00"), Skip("10:00"), Skip("11:00", "Meeting ran long"),
            Row("12:00", PlannerTaskStatus.Cancelled, null, SkipReasonKey.NotRelevant));

        summary.SkippedCount.Should().Be(4);
        summary.Reasons.Should().ContainEquivalentOf(new SkipSummaryService.ReasonCount(null, null, 2));
        summary.Reasons.Sum(r => r.Count).Should().Be(summary.SkippedCount);
    }

    /// <summary>
    /// No cap on groups: a truncated list could drop two groups the client would fold into the largest one.
    /// </summary>
    [Fact]
    public void Aggregate_ManyDistinctReasons_AreAllReturned()
    {
        var rows = Enumerable.Range(0, 40).Select(i => Skip("09:00", $"reason {i}")).ToArray();

        Aggregate(rows).Reasons.Should().HaveCount(40);
    }

    /// <summary>
    /// The denominator is everything planned, in any status. Completed, untouched and skipped tasks all count
    /// as planned; only the skipped one counts as skipped.
    /// </summary>
    [Fact]
    public void Aggregate_PlannedCount_IncludesEveryStatus()
    {
        var summary = Aggregate(
            Row("21:00", PlannerTaskStatus.Completed),
            Row("21:15", PlannerTaskStatus.NotStarted),
            Row("21:30", PlannerTaskStatus.OnHold),
            Row("21:45", PlannerTaskStatus.InProgress),
            Skip("21:59", "Too tired"));

        summary.ByStartHour.Should().ContainSingle()
            .Which.Should().Be(new SkipSummaryService.HourCount(21, 5, 1));
        summary.SkippedCount.Should().Be(1);
    }

    /// <summary>
    /// Only Cancelled is a skip. An untouched task on a day that is over is not one — the trend already counts
    /// it as never happened, and it must not be counted twice across the two lines.
    /// </summary>
    [Fact]
    public void Aggregate_UntouchedTask_IsPlannedButNotSkipped()
    {
        var summary = Aggregate(Row("09:00"));

        summary.SkippedCount.Should().Be(0);
        summary.Reasons.Should().BeEmpty();
        summary.ByStartHour.Should().Equal(new SkipSummaryService.HourCount(9, 1, 0));
    }

    /// <summary>Hours with nothing planned are omitted, and the rest come back ascending.</summary>
    [Fact]
    public void Aggregate_ByStartHour_OmitsEmptyHours_AndIsAscending()
    {
        var summary = Aggregate(Skip("21:00"), Row("09:00"), Row("09:30"), Skip("00:10"));

        summary.ByStartHour.Should().Equal(
            new SkipSummaryService.HourCount(0, 1, 1),
            new SkipSummaryService.HourCount(9, 2, 0),
            new SkipSummaryService.HourCount(21, 1, 1));
        summary.ByStartHour.Sum(h => h.SkippedCount).Should().Be(summary.SkippedCount);
    }

    /// <summary>A task wrapping midnight counts in the hour it starts, not the one it ends.</summary>
    [Fact]
    public void Aggregate_TaskStartingBeforeMidnight_CountsInItsStartHour()
    {
        Aggregate(Skip("23:30")).ByStartHour.Should().Equal(new SkipSummaryService.HourCount(23, 1, 1));
    }
}
