namespace MojaDigitalnaFirma.Kernel.scheduling;

/// <summary>
/// The keyed strategy an owning module implements for the work itself — the old job body, now keyed.
/// The Scheduler module discovers implementations by <see cref="Key"/> and invokes
/// <see cref="ExecuteAsync"/> with failure isolation + run-log capture (phase 03).
/// <para>
/// Handlers run with <b>no authenticated user</b> (background context). They must be background-safe:
/// resolve their own scope, never assume a logged-in principal, and never put PII into anything the
/// Scheduler records (the run log captures only the outcome and a non-PII error type/message).
/// </para>
/// </summary>
public interface IScheduledJobHandler
{
    /// <summary>Stable key matched against <see cref="RecurringJobRegistration.HandlerKey"/>.</summary>
    string Key { get; }

    Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct);
}