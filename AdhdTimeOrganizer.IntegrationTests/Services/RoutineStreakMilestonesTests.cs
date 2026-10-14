using AdhdTimeOrganizer.Routines.domain.service;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Services;

/// <summary>
/// P3-02. Pins the milestone table in each unit the client names a run in. The client has no copy of the
/// table and reads <c>NextStreakMilestone</c> instead, so a change here changes the confetti and the bell
/// together, and this test is what notices.
/// </summary>
public class RoutineStreakMilestonesTests
{
    [Theory(DisplayName = "P3-02: milestones are about a week, a month, a quarter, half a year and every year, in the period's own count")]
    [InlineData(1, new[] { 7, 30, 90, 180, 365, 730 })]
    [InlineData(7, new[] { 4, 13, 26, 52, 104 })]
    [InlineData(30, new[] { 3, 6, 12, 24 })]
    [InlineData(365, new[] { 2, 3, 4 })]
    public void Milestones_PerPeriodLength(int lengthInDays, int[] expected)
    {
        var walked = new List<int>();
        var streak = 0;
        while (walked.Count < expected.Length)
        {
            streak = RoutineStreakMilestones.Next(streak, lengthInDays);
            walked.Add(streak);
        }

        walked.Should().Equal(expected);
    }

    [Theory]
    [InlineData(7, 1, true)]
    [InlineData(30, 1, true)]
    [InlineData(8, 1, false)]
    [InlineData(29, 1, false)]
    [InlineData(4, 7, true)]
    [InlineData(7, 7, false)]
    [InlineData(30, 7, false)]
    [InlineData(1, 7, false)]
    [InlineData(1, 30, false)]
    [InlineData(0, 1, false)]
    public void IsMilestone(int streak, int lengthInDays, bool expected)
    {
        RoutineStreakMilestones.IsMilestone(streak, lengthInDays).Should().Be(expected);
    }

    /// <summary>Every milestone <see cref="RoutineStreakMilestones.Next"/> reports is one <c>IsMilestone</c> agrees with.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(14)]
    [InlineData(30)]
    [InlineData(200)]
    [InlineData(365)]
    public void Next_AndIsMilestone_Agree(int lengthInDays)
    {
        for (var streak = 0; streak < 400; streak++)
        {
            var next = RoutineStreakMilestones.Next(streak, lengthInDays);
            next.Should().BeGreaterThan(streak);
            RoutineStreakMilestones.IsMilestone(next, lengthInDays).Should().BeTrue();
            for (var between = streak + 1; between < next; between++)
                RoutineStreakMilestones.IsMilestone(between, lengthInDays).Should().BeFalse();
        }
    }
}
