using AdhdTimeOrganizer.Routines.domain.model.@enum;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;

namespace AdhdTimeOrganizer.Routines.domain.serviceContract;

/// <summary>
/// Turns routine-period events into notifications. The only place in the portal that knows which
/// <c>NotificationType</c> and payload each routine event maps to — every caller stays a one-liner, the same
/// split <see cref="IReminderRegistrationService"/> uses for the Reminders module.
/// <para>
/// Every method is <b>best-effort</b>: a notification that fails must never roll back the reset that caused
/// it. Implementations swallow and log rather than throw, so callers do not need try/catch.
/// </para>
/// </summary>
public interface IRoutinePeriodNotificationService
{
    /// <summary>
    /// A period just reset. Called from <i>every</i> site that applies a reset, not only the nightly job:
    /// <c>RoutineResetService.TryReset</c> is also reached lazily from the grouped read and the toggle
    /// endpoint, and whichever path gets there first is the one that owns the announcement.
    /// </summary>
    Task NotifyPeriodEndedAsync(RoutineTimePeriod period, RoutinePeriodCompletion completion, StreakOutcome outcome, CancellationToken ct = default);

    /// <summary>
    /// The lead-time nudge, with counts taken by the sweep at nudge time. Returns true when a notification was
    /// actually raised, which is the caller's cue to stamp <c>EndingSoonNotifiedFor</c> — a period that turns
    /// out to have nothing outstanding is deliberately left unmarked so it can be nudged tomorrow instead.
    /// </summary>
    Task<bool> NotifyEndingSoonAsync(RoutineTimePeriod period, RoutineResetService.RoutineNudge nudge, int remaining, int total, CancellationToken ct = default);

    /// <summary>
    /// The period's streak grace window is about to lapse. Returns true when a notification was actually
    /// raised, which is the caller's cue to stamp <c>GraceNotifiedFor</c> — a dispatch that was swallowed
    /// must leave the period unmarked, or the grace window lapses and breaks the streak with no warning
    /// having reached the user.
    /// </summary>
    Task<bool> NotifyGraceExpiringAsync(RoutineTimePeriod period, CancellationToken ct = default);

    /// <summary>
    /// The once-a-day digest of the items suggested for the user's <paramref name="date"/> and not done yet —
    /// one notification for all of them, never one per item. <paramref name="items"/> must have
    /// <c>Activity</c> loaded, since its name is what the body lists. Returns false when nothing was raised
    /// (an empty list, or a swallowed dispatch), which is the caller's cue to leave the day unmarked so a later
    /// run that morning can retry.
    /// </summary>
    Task<bool> NotifySuggestedTodayAsync(long userId, DateOnly date, IReadOnlyList<RoutineTodoList> items, CancellationToken ct = default);
}