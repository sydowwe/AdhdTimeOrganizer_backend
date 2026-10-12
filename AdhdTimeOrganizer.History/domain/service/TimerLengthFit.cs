using AdhdTimeOrganizer.Core.domain.rule;
using AdhdTimeOrganizer.History.domain.model.@enum;

namespace AdhdTimeOrganizer.History.domain.service;

/// <summary>
/// F2-07: the focus length that usually works for an activity, from how its timer sessions ended. Pure: the caller
/// reads the sessions, and this turns them into stretches and picks a length.
/// </summary>
/// <remarks>
/// <para>
/// <b>One sample is one unbroken stretch of focus</b>: a countdown's single row, or one focus row of a Pomodoro.
/// Breaks are never stretches. They are logged against the rest activity, which may be this same activity, so
/// they are told apart by phase and not by activity.
/// </para>
/// <para>
/// <b>A stretch that ran its full length says "at least this long", not "exactly this long".</b> A completed
/// 10-minute countdown tells nothing about 15 minutes, so it is left out of that length's count rather than
/// counted as a failure. Only a stretch cut short by Stop says where attention ran out. That keeps a user who sets
/// short timers and finishes them from reading as someone who cannot do longer ones.
/// </para>
/// <para>
/// The suggestion is the largest whole <see cref="StepSeconds"/> length that at least
/// <see cref="EstimateFitRule.TimerLengthReachedShare"/> of the stretches bearing on it reached, with at least
/// <see cref="EstimateFitRule.TimerLengthMinimumSamples"/> of them, over at least as many sessions. Below that it
/// is nothing. It is never a share or a count of failures. It describes the length, not the person.
/// </para>
/// <para>
/// <see cref="TimerSessionOutcome.RanOutWhileAway"/> sessions are left out whole. The timer ran while nobody was
/// there, so it shows neither focus nor its end.
/// </para>
/// </remarks>
public static class TimerLengthFit
{
    /// <summary>Lengths are offered in whole steps of five minutes ("Try 15 min"), never as 13m 40s.</summary>
    public const long StepSeconds = 5 * 60;

    /// <summary>Newest sessions read per activity, recent enough to follow a user whose focus has changed.</summary>
    public const int SessionLookback = 20;

    /// <summary>How far back a session still counts.</summary>
    public const int WindowDays = 90;

    /// <param name="Seconds">How long the stretch lasted.</param>
    /// <param name="RanItsLength">True when the stretch ended because its time was up; false when Stop ended it.</param>
    public readonly record struct Stretch(long Seconds, bool RanItsLength);

    /// <param name="SuggestedSeconds">The length to offer, a whole number of <see cref="StepSeconds"/>.</param>
    /// <param name="SampleCount">The stretches that bore on that length.</param>
    public readonly record struct Fit(long SuggestedSeconds, int SampleCount);

    /// <summary>
    /// The focus stretches of one session.
    /// </summary>
    /// <param name="focusRowSeconds">The session's focus rows of the activity, in start order.</param>
    public static IReadOnlyList<Stretch> StretchesOf(
        TimerSessionKind kind, TimerSessionOutcome outcome, int? completedFocusPeriods, IReadOnlyList<long> focusRowSeconds)
    {
        if (outcome == TimerSessionOutcome.RanOutWhileAway || focusRowSeconds.Count == 0)
            return [];

        if (kind == TimerSessionKind.Timer)
            return [new Stretch(focusRowSeconds[0], outcome == TimerSessionOutcome.Completed)];

        // A Pomodoro's first CompletedFocusPeriods focus rows ran their length. A row after them can only be the one
        // Stop cut short. If the session was stopped during a break there is no such row, and no stretch was cut.
        if (completedFocusPeriods is not { } completed)
            return [];

        var reached = Math.Min(completed, focusRowSeconds.Count);
        var stretches = focusRowSeconds.Take(reached).Select(s => new Stretch(s, true)).ToList();
        if (outcome == TimerSessionOutcome.StoppedEarly && focusRowSeconds.Count > reached)
            stretches.Add(new Stretch(focusRowSeconds[^1], false));
        return stretches;
    }

    /// <summary>The length to offer, or <c>null</c> when the sessions do not say enough.</summary>
    /// <param name="sessionCount">The sessions the stretches came from.</param>
    public static Fit? Suggest(IReadOnlyCollection<Stretch> stretches, int sessionCount)
    {
        if (sessionCount < EstimateFitRule.TimerLengthMinimumSamples || stretches.Count == 0)
            return null;

        Fit? best = null;
        var longest = stretches.Max(s => s.Seconds);
        for (var length = StepSeconds; length <= longest; length += StepSeconds)
        {
            var reached = stretches.Count(s => s.Seconds >= length);
            var cutShort = stretches.Count(s => !s.RanItsLength && s.Seconds < length);
            var bearing = reached + cutShort;

            if (bearing >= EstimateFitRule.TimerLengthMinimumSamples
                && reached >= EstimateFitRule.TimerLengthReachedShare * bearing)
                best = new Fit(length, bearing);
        }

        return best;
    }
}
