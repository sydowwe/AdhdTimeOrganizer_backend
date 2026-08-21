using AdhdTimeOrganizer.History.domain.model.entity.timerAlarm;

namespace AdhdTimeOrganizer.History.domain.serviceContract;

/// <summary>
/// Registers and cancels the one-shot alarms that fire at a running timer's phase boundaries.
/// <para>
/// The interface exists because the module DI scan registers marker-carrying services
/// <c>AsImplementedInterfaces()</c> — a class marked only <c>IScopedService</c> resolves as nothing useful.
/// Consumers depend on this; the mapping to rows and Scheduler triggers lives in the implementation.
/// </para>
/// </summary>
public interface ITimerBoundaryAlarmService
{
    /// <summary>
    /// Replaces the whole alarm set for one timer session. Idempotent: re-sending a set cancels whatever was
    /// registered under the session id first, so a client that is unsure what landed may simply re-send.
    /// Boundaries already in the past are dropped rather than fired immediately.
    /// </summary>
    /// <returns>How many alarms were actually registered.</returns>
    Task<int> ReplaceSessionAsync(long userId, Guid sessionId, IReadOnlyCollection<TimerBoundaryAlarm> boundaries, CancellationToken ct = default);

    /// <summary>Cancels every alarm for one session. A no-op on an unknown session, never a 404.</summary>
    Task CancelSessionAsync(long userId, Guid sessionId, CancellationToken ct = default);

    /// <summary>Removes the triggers for these alarms and deletes their rows.</summary>
    Task CancelAsync(IReadOnlyCollection<TimerBoundaryAlarm> alarms, CancellationToken ct = default);

    /// <summary>Registers one alarm's one-shot trigger. Idempotent by job key.</summary>
    Task RegisterTriggerAsync(TimerBoundaryAlarm alarm, CancellationToken ct = default);
}
