namespace MojaDigitalnaFirma.Kernel.scheduling;

/// <summary>
/// Registration/control seam for recurring background jobs, implemented by the Scheduler module
/// (Core.Scheduler) and consumed by owning modules and the dashboard. Owners depend on this contract,
/// never on the Scheduler module. Implemented in phase 02b.
/// </summary>
public interface IScheduler
{
    /// <summary>
    /// Idempotent upsert by <see cref="RecurringJobRegistration.JobKey"/>: creates the registry row +
    /// trigger, or reschedules in place. Re-calling with the same key never duplicates.
    /// </summary>
    Task RegisterRecurringJobAsync(RecurringJobRegistration registration, CancellationToken ct);

    /// <summary>Unschedule + mark removed. Idempotent no-op on an unknown key.</summary>
    Task RemoveJobAsync(string jobKey, CancellationToken ct);

    /// <summary>Pause a job's schedule (it stops firing until resumed). Idempotent.</summary>
    Task PauseJobAsync(string jobKey, CancellationToken ct);

    /// <summary>Resume a paused job. Idempotent.</summary>
    Task ResumeJobAsync(string jobKey, CancellationToken ct);

    /// <summary>Fire the job once immediately, off-schedule.</summary>
    Task TriggerNowAsync(string jobKey, CancellationToken ct);
}