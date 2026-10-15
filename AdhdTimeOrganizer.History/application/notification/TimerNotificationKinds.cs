using AdhdTimeOrganizer.History.domain.model.@enum;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.kind;
using Sydowwe.Framework.Contracts.notification.text;
using Sydowwe.Framework.domain.@enum;

namespace AdhdTimeOrganizer.History.application.notification;

/// <summary>
/// A running timer crossed a phase boundary — a focus period ended, a break ended, or a plain countdown finished.
/// Scheduled as a one-shot Scheduler job the moment the user presses Start, so the alarm no longer depends on the tab
/// that started it still being open (a <c>setTimeout</c> cannot fire in a closed tab).
/// <para>
/// The title says <i>what ended</i> — that is the whole message, and it has to read correctly on a lock screen with
/// the body truncated away; the body carries the context: which activity, and where in the cycle the user is. Every
/// context field is optional and dropped when absent, so a plain countdown renders the bare lead with no dangling
/// separators, and an empty document still renders.
/// </para>
/// </summary>
public sealed class TimerBoundaryNotificationKind : NotificationKind<TimerBoundaryPayload>, ISingletonService
{
    public override NotificationType Type => HistoryNotificationTypes.TimerBoundary;

    /// <summary>
    /// The only kind that is deliberately quiet-hours-exempt: the user scheduled this instant themselves, minutes ago,
    /// and deferring it to the end of a quiet window delivers "focus period ended" hours late — worse than not
    /// delivering it. Not held while away, for the same reason. Not mailed: nobody wants a mail per pomodoro.
    /// </summary>
    public override bool QuietHoursExempt => true;

    protected override (string Title, string Body) Render(TimerBoundaryPayload? payload, NotificationRenderContext context)
    {
        var w = context.Kit.Language == AvailableLocales.En ? Words.En : Words.Sk;
        if (payload is null)
            return (w.TimerEnded, w.CountdownEnded);

        var title = payload.Phase switch
        {
            TimerBoundaryPhase.Focus => w.FocusEnded,
            TimerBoundaryPhase.ShortBreak => w.ShortBreakEnded,
            TimerBoundaryPhase.LongBreak => w.LongBreakEnded,
            _ => w.TimerEnded
        };

        var lead = payload.Phase switch
        {
            TimerBoundaryPhase.Focus => w.TimeForBreak,
            TimerBoundaryPhase.ShortBreak or TimerBoundaryPhase.LongBreak => w.BackToWork,
            _ => w.CountdownEnded
        };

        var parts = new List<string>();
        if (NonEmpty(payload.ActivityName) is { } activity)
            parts.Add(activity);
        if (payload.PhaseIndex is { } phaseIndex)
            parts.Add($"{w.Phase} {Fraction(phaseIndex, payload.PhaseTotal)}");
        if (payload.CycleIndex is { } cycleIndex)
            parts.Add($"{w.Cycle} {Fraction(cycleIndex, payload.CycleTotal)}");

        return (title, parts.Count > 0 ? $"{string.Join(" · ", parts)}. {lead}" : lead);
    }

    /// <summary>The tag is what makes the push <b>replace</b> the tab's own notification; a null one reintroduces the double alarm.</summary>
    protected override (string? Tag, string? Url) PushMeta(TimerBoundaryPayload? payload) => (payload?.Tag, payload?.Url);

    private static string Fraction(int index, int? total) => total is { } t ? $"{index}/{t}" : $"{index}";

    private sealed record Words(
        string TimerEnded, string FocusEnded, string ShortBreakEnded, string LongBreakEnded,
        string CountdownEnded, string TimeForBreak, string BackToWork, string Phase, string Cycle)
    {
        public static readonly Words Sk = new(
            "Časovač skončil", "Sústredenie skončilo", "Krátka prestávka skončila", "Dlhá prestávka skončila",
            "Odpočet skončil.", "Čas na prestávku!", "Späť do práce!", "Fáza", "Cyklus");

        public static readonly Words En = new(
            "Timer finished", "Focus finished", "Short break over", "Long break over",
            "The countdown is over.", "Time for a break!", "Back to work!", "Phase", "Cycle");
    }
}

/// <summary>
/// A stopwatch has been running a long while — "still on X? 1 h 30 min". A pomodoro or a countdown rings by itself
/// and a stopwatch never does, so hyperfocus and a stopwatch the user forgot about both end the same way: an
/// afternoon gone, or a nine-hour history row to fix. Registered by the client as a one-shot per check-in while the
/// stopwatch runs (the same per-instant triggers as <see cref="TimerBoundaryNotificationKind"/>), and cancelled on
/// pause and stop.
/// <para>
/// The title asks the question, with the activity when there is one; the body says how long the stopwatch has been
/// running. Each half degrades on its own.
/// </para>
/// </summary>
public sealed class StopwatchCheckInNotificationKind : NotificationKind<StopwatchCheckInPayload>, ISingletonService
{
    public override NotificationType Type => HistoryNotificationTypes.StopwatchCheckIn;

    // NOT quiet-hours-exempt, unlike the timer boundary: the user picked when a timer ends, but the app picked when to
    // check in. Deferring would be wrong too — "still on X?" at 07:00 about a stopwatch from 01:00 is noise — so the
    // producer skips a check-in that falls inside the recipient's quiet hours rather than letting the dispatcher hold
    // it. Not held while away: the user started this stopwatch. Not mailed: a mail an hour is noise. No buttons: Stop
    // is client state, so a tap opens the stopwatch page instead.

    protected override (string Title, string Body) Render(StopwatchCheckInPayload? payload, NotificationRenderContext context)
    {
        var kit = context.Kit;
        var activity = NonEmpty(payload?.ActivityName);
        var minutes = payload?.ElapsedMinutes is int m and > 0 ? m : (int?)null;

        return kit.Language == AvailableLocales.En
            ? (
                // A question, not a reproach — the user may well still be on it, and that is fine.
                activity is null ? "Still at it?" : $"Still on {activity}?",
                minutes is { } enMinutes
                    ? $"The stopwatch has been running for {NotificationTextKit.Duration(enMinutes)}."
                    : "Your stopwatch is still running.")
            : (
                // "Stále pri „Čítanie“?" — the quoted name stays in the nominative.
                activity is null ? "Stále pri tom?" : $"Stále pri {kit.Quote(activity)}?",
                minutes is { } skMinutes
                    ? $"Stopky bežia už {NotificationTextKit.Duration(skMinutes)}."
                    : "Stopky stále bežia.");
    }

    /// <summary>The tag replaces a second copy of the same check-in in place; the url is the client's route to the stopwatch.</summary>
    protected override (string? Tag, string? Url) PushMeta(StopwatchCheckInPayload? payload) => (payload?.Tag, payload?.Url);
}
