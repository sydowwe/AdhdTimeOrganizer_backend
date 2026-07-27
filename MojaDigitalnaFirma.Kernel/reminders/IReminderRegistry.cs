namespace MojaDigitalnaFirma.Kernel.reminders;

/// <summary>
/// Registration/control seam for scheduled reminders, implemented by the Reminders module
/// (Core.Reminders) and consumed by owning modules. Owners depend on this contract, never on the
/// Reminders module — the arrow points owning module → Kernel ← Reminders. Implemented in phase 02.
/// <para>
/// Owners declare/withdraw reminders as data: they re-register on a date change (idempotent upsert by
/// <see cref="ReminderKey"/>) and cancel on entity deletion. This module scans and dispatches; it never
/// pulls from domain modules.
/// </para>
/// </summary>
public interface IReminderRegistry
{
    /// <summary>
    /// Idempotent upsert by <see cref="ReminderRegistration.Key"/>: inserts a new definition or updates the
    /// existing one in place. Re-calling with the same key never duplicates. The result reports which happened.
    /// </summary>
    Task<ReminderRegistrationResult> RegisterAsync(ReminderRegistration registration, CancellationToken ct);

    /// <summary>Cancel (withdraw) the reminder for this key. Idempotent no-op on an unknown key.</summary>
    Task CancelAsync(ReminderKey key, CancellationToken ct);

    /// <summary>Pause the reminder (no occurrences dispatched until resumed). Idempotent.</summary>
    Task PauseAsync(ReminderKey key, CancellationToken ct);

    /// <summary>Resume a paused reminder. Idempotent.</summary>
    Task ResumeAsync(ReminderKey key, CancellationToken ct);
}