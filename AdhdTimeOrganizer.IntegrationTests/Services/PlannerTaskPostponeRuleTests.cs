using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.service;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Services;

/// <summary>
/// FO3 phase 2's planner rule without a database: which moves are postponements, and what the first one fixes.
/// The write paths that feed it (PUT, span patch) are pinned over HTTP in <c>Endpoints.PlannerTaskPostponeTests</c>.
/// <para>The clock is fixed at 2026-10-07 12:00 on the user's wall clock.</para>
/// </summary>
public class PlannerTaskPostponeRuleTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);
    private static readonly DateTime Now = Today.ToDateTime(new TimeOnly(12, 0));

    private static PlannerTask Task() => new()
    {
        Status = PlannerTaskStatus.NotStarted,
        StartTime = new TimeOnly(9, 0),
        EndTime = new TimeOnly(10, 0),
        IsBackground = false
    };

    private static bool Move(PlannerTask task, DateOnly fromDate, int fromHour, DateOnly toDate, int toHour,
        PlannerTaskStatus statusBefore = PlannerTaskStatus.NotStarted) =>
        PlannerTaskPostponeRule.Apply(task, statusBefore, fromDate, new TimeOnly(fromHour, 0), toDate, new TimeOnly(toHour, 0), Now);

    [Fact(DisplayName = "FO3: pushing a block whose start has passed later in its day is a postponement, and fixes the original slot")]
    public void LaterMove_AfterTheStartPassed_Counts()
    {
        var task = Task();

        Move(task, Today, 9, Today, 14).Should().BeTrue();

        task.PostponeCount.Should().Be(1);
        task.OriginalDate.Should().Be(Today);
        task.OriginalStartTime.Should().Be(new TimeOnly(9, 0));
    }

    [Fact(DisplayName = "FO3: moving a missed block to a later day counts too")]
    public void LaterDay_Counts()
    {
        var task = Task();

        Move(task, Today.AddDays(-1), 9, Today, 9).Should().BeTrue();

        task.OriginalDate.Should().Be(Today.AddDays(-1));
    }

    [Fact(DisplayName = "FO3: tidying up a block that has not started yet is a re-plan, not a postponement")]
    public void LaterMove_BeforeTheStart_DoesNotCount()
    {
        var task = Task();

        Move(task, Today, 15, Today, 17).Should().BeFalse();
        Move(task, Today.AddDays(1), 9, Today.AddDays(2), 9).Should().BeFalse();

        task.PostponeCount.Should().Be(0);
        task.OriginalDate.Should().BeNull();
        task.OriginalStartTime.Should().BeNull();
    }

    [Fact(DisplayName = "FO3: moving a block earlier, or not at all, never counts")]
    public void EarlierOrSameSlot_DoesNotCount()
    {
        var task = Task();

        Move(task, Today, 9, Today, 8).Should().BeFalse();
        Move(task, Today, 9, Today, 9).Should().BeFalse();
        Move(task, Today, 9, Today.AddDays(-1), 11).Should().BeFalse();

        task.PostponeCount.Should().Be(0);
    }

    [Theory(DisplayName = "FO3: only a block nobody started can be postponed")]
    [InlineData(PlannerTaskStatus.InProgress)]
    [InlineData(PlannerTaskStatus.OnHold)]
    [InlineData(PlannerTaskStatus.Completed)]
    [InlineData(PlannerTaskStatus.Cancelled)]
    public void StartedOrClosed_DoesNotCount(PlannerTaskStatus status)
    {
        var task = Task();

        Move(task, Today, 9, Today, 14, status).Should().BeFalse();

        task.PostponeCount.Should().Be(0);
    }

    /// <summary>
    /// The rule reads the <b>original</b> slot, not the one it is moving from. A block snoozed from 09:00 to 15:00
    /// and pushed again at noon, before 15:00 arrives, is still slipping.
    /// </summary>
    [Fact(DisplayName = "FO3: once a block has slipped, every further push counts, and the original slot is kept")]
    public void AfterTheFirstSlip_EveryPushCounts_AndTheOriginalStays()
    {
        var task = Task();
        Move(task, Today, 9, Today, 15).Should().BeTrue();

        Move(task, Today, 15, Today, 17).Should().BeTrue("the 15:00 slot is still ahead, but 09:00 is not");

        task.PostponeCount.Should().Be(2);
        task.OriginalStartTime.Should().Be(new TimeOnly(9, 0));
    }

    /// <summary>
    /// A row carried over before <c>OriginalStartTime</c> existed has a date and no time. Pairing that old date
    /// with the time it is moved from today would invent a slot that never existed.
    /// </summary>
    [Fact(DisplayName = "FO3: a row carried over before this change keeps its date and gains no invented time")]
    public void RowCarriedBeforeTheColumn_KeepsItsDateAlone()
    {
        var task = Task();
        task.OriginalDate = Today.AddDays(-3);

        Move(task, Today, 14, Today, 16).Should().BeTrue("its original day is long past");

        task.OriginalDate.Should().Be(Today.AddDays(-3));
        task.OriginalStartTime.Should().BeNull();
    }

    [Fact(DisplayName = "FO3: drift is measured from the original start only while the block is still on its original day")]
    public void DriftAnchor_IsTheOriginalStart_OnlyOnTheOriginalDay()
    {
        var nine = new TimeOnly(9, 0);
        var two = new TimeOnly(14, 0);

        PlannerTaskPostponeRule.DriftAnchor(Today, two, Today, nine).Should().Be(nine, "snoozed within its day");
        PlannerTaskPostponeRule.DriftAnchor(Today, two, Today.AddDays(-1), nine).Should().Be(two, "carried: a fresh attempt");
        PlannerTaskPostponeRule.DriftAnchor(Today, two, null, null).Should().Be(two, "never slipped");
        PlannerTaskPostponeRule.DriftAnchor(Today, two, Today, null).Should().Be(two, "an original date with no time");
    }
}
