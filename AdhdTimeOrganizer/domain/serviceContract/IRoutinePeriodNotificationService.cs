using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.domain.service;

namespace AdhdTimeOrganizer.domain.serviceContract;

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

    /// <summary>The period's streak grace window is about to lapse.</summary>
    Task NotifyGraceExpiringAsync(RoutineTimePeriod period, CancellationToken ct = default);
}