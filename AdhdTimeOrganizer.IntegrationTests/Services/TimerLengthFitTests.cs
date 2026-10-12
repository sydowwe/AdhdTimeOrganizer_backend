using AdhdTimeOrganizer.Core.domain.rule;
using AdhdTimeOrganizer.History.domain.model.@enum;
using AdhdTimeOrganizer.History.domain.service;
using FluentAssertions;
using Xunit;
using Stretch = AdhdTimeOrganizer.History.domain.service.TimerLengthFit.Stretch;

namespace AdhdTimeOrganizer.IntegrationTests.Services;

/// <summary>
/// F2-07's rule without a database: what a stretch of focus is, and which length is offered. The read that applies it
/// is <c>Endpoints.ActivityHistorySessionFitTests</c>.
/// </summary>
public class TimerLengthFitTests
{
    private static long Min(int minutes) => minutes * 60L;

    private static Stretch Stopped(int minutes) => new(Min(minutes), RanItsLength: false);

    private static Stretch Ran(int minutes) => new(Min(minutes), RanItsLength: true);

    /// <summary>F2-07's moment: a 25-minute focus that is stopped around minute 12 nine times out of ten.</summary>
    [Fact(DisplayName = "F2-07: stopped around minute 12 most times, the offer is 10 minutes")]
    public void UsuallyStoppedEarly_OffersTheLengthThatIsReached()
    {
        var stretches = Enumerable.Repeat(Stopped(12), 9).Append(Ran(25)).ToList();

        TimerLengthFit.Suggest(stretches, sessionCount: 10)
            .Should().Be(new TimerLengthFit.Fit(Min(10), 10));
    }

    [Fact(DisplayName = "F2-07: a length that is always finished is offered as it is")]
    public void AlwaysFinished_OffersThatLength() =>
        TimerLengthFit.Suggest(Enumerable.Repeat(Ran(25), 6).ToList(), sessionCount: 6)
            .Should().Be(new TimerLengthFit.Fit(Min(25), 6));

    /// <summary>
    /// Ten-minute timers that were finished say nothing about 25 minutes. Counted as failures there, they would
    /// pull the offer down to 10 for a user who finishes every 25 they set.
    /// </summary>
    [Fact(DisplayName = "F2-07: a short timer that ran its length is not counted against a longer one")]
    public void FinishedShortTimers_DoNotCountAgainstLongerLengths()
    {
        var stretches = Enumerable.Repeat(Ran(10), 5).Concat(Enumerable.Repeat(Ran(25), 5)).ToList();

        TimerLengthFit.Suggest(stretches, sessionCount: 10)
            .Should().Be(new TimerLengthFit.Fit(Min(25), 5));
    }

    [Fact(DisplayName = "F2-07: below five sessions, or when not even five minutes is usually reached, it says nothing")]
    public void TooFewOrNothingReached_IsSilent()
    {
        TimerLengthFit.Suggest(Enumerable.Repeat(Ran(25), 8).ToList(), EstimateFitRule.TimerLengthMinimumSamples - 1)
            .Should().BeNull("one long Pomodoro must not carry the figure alone");
        TimerLengthFit.Suggest(Enumerable.Repeat(Stopped(3), 6).ToList(), sessionCount: 6)
            .Should().BeNull();
    }

    [Fact(DisplayName = "F2-07: a Pomodoro's finished focus periods ran their length, and only the period Stop cut is a stop")]
    public void Pomodoro_StretchesFollowTheCompletedPeriods()
    {
        TimerLengthFit.StretchesOf(TimerSessionKind.Pomodoro, TimerSessionOutcome.StoppedEarly, 2, [Min(25), Min(25), Min(8)])
            .Should().Equal(Ran(25), Ran(25), Stopped(8));

        TimerLengthFit.StretchesOf(TimerSessionKind.Pomodoro, TimerSessionOutcome.StoppedEarly, 2, [Min(25), Min(25)])
            .Should().Equal([Ran(25), Ran(25)], "stopped during a break, no focus period was cut");

        TimerLengthFit.StretchesOf(TimerSessionKind.Pomodoro, TimerSessionOutcome.Completed, 4, [Min(25), Min(25), Min(25), Min(25)])
            .Should().AllSatisfy(s => s.RanItsLength.Should().BeTrue());
    }

    [Fact(DisplayName = "F2-07: a countdown is one stretch, and a session that ran out while away is none")]
    public void Countdown_AndAwaySessions()
    {
        TimerLengthFit.StretchesOf(TimerSessionKind.Timer, TimerSessionOutcome.StoppedEarly, null, [Min(12)])
            .Should().Equal(Stopped(12));
        TimerLengthFit.StretchesOf(TimerSessionKind.Timer, TimerSessionOutcome.Completed, null, [Min(25)])
            .Should().Equal(Ran(25));
        TimerLengthFit.StretchesOf(TimerSessionKind.Pomodoro, TimerSessionOutcome.RanOutWhileAway, 1, [Min(25), Min(25)])
            .Should().BeEmpty("the timer ran while nobody was there");
    }
}
