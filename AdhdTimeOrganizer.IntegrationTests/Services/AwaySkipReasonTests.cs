using AdhdTimeOrganizer.Planning.application.dto;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.service;
using FluentAssertions;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Services;

/// <summary>
/// FO5's <see cref="SkipReasonKey.Away"/>: written by the welcome-back fresh start alone, and invisible to the skip
/// insight, which would otherwise read a week of blocks closed at once as "you skip everything at 9:00".
/// </summary>
public class AwaySkipReasonTests
{
    [Fact(DisplayName = "FO5: 'away' goes out on the wire but cannot be sent in — the skip sheet never offers it")]
    public void Away_IsWrittenButNotParsed()
    {
        SkipReasonKeyTokens.Token(SkipReasonKey.Away).Should().Be("away");
        SkipReasonKeyTokens.TryParse("away", out _).Should().BeFalse();
        SkipReasonKeyTokens.TryParse("noTime", out var key).Should().BeTrue();
        key.Should().Be(SkipReasonKey.NoTime);
    }

    [Fact(DisplayName = "FO5: the skip summary leaves away-closed blocks out — neither planned nor skipped")]
    public void SkipSummary_IgnoresAway()
    {
        var nine = new TimeOnly(9, 0);
        var summary = SkipSummaryService.Aggregate(
        [
            new(nine, PlannerTaskStatus.Cancelled, null, SkipReasonKey.Away),
            new(nine, PlannerTaskStatus.Cancelled, null, SkipReasonKey.Away),
            new(nine, PlannerTaskStatus.Cancelled, null, SkipReasonKey.NoTime),
            new(nine, PlannerTaskStatus.Completed, null)
        ]);

        summary.SkippedCount.Should().Be(1);
        summary.Reasons.Should().ContainSingle().Which.ReasonKey.Should().Be(SkipReasonKey.NoTime);
        summary.ByStartHour.Should().ContainSingle().Which.Should().Be(new SkipSummaryService.HourCount(9, 2, 1));
    }
}
