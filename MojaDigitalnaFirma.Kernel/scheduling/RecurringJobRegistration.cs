namespace MojaDigitalnaFirma.Kernel.scheduling;

/// <summary>
/// The upsert payload an owning module passes to <see cref="IScheduler.RegisterRecurringJobAsync"/>.
/// Identified by <see cref="JobKey"/> — re-registering the same key reschedules in place (idempotent),
/// so a module can reconcile its schedule on every boot. Free of any domain type: the work is named by
/// <see cref="HandlerKey"/> and parameterised only by an opaque <see cref="Payload"/>.
/// </summary>
public sealed class RecurringJobRegistration
{
    /// <summary>Unique, stable identity of the schedule (the idempotency key).</summary>
    public required string JobKey { get; init; }

    /// <summary>Key of the <see cref="IScheduledJobHandler"/> the dispatcher invokes for this job.</summary>
    public required string HandlerKey { get; init; }

    /// <summary>Owning module label, for grouping/labels in the dashboard (e.g. "Reminders").</summary>
    public required string OwnerModule { get; init; }

    /// <summary>How often the job recurs.</summary>
    public required ScheduleSpec Schedule { get; init; }

    /// <summary>What to do on a missed fire. Defaults to <see cref="MisfirePolicy.FireAndProceed"/>.</summary>
    public MisfirePolicy MisfirePolicy { get; init; } = MisfirePolicy.FireAndProceed;

    /// <summary>
    /// Opaque payload serialised to JSON and handed back on the <see cref="ScheduledJobContext"/>. It is
    /// persisted verbatim (audit-exempt but not encrypted) and logged on failure, so it must contain no
    /// PII: pass ids/keys the handler can look up, never a person's name, address, or other free text.
    /// </summary>
    public object? Payload { get; init; }

    /// <summary>If true, the dispatcher will not run this job's handler while a prior fire is still running.</summary>
    public bool DisallowConcurrent { get; init; }

    /// <summary>
    /// How many times the dispatcher auto-retries a <b>scheduled</b> fire that ends in <c>Failed</c>, before
    /// giving up. Default <b>3</b>; set to <c>0</c> to disable retries for this job. Each retry is an
    /// off-schedule re-fire with incremental (exponential) backoff (~1 min → 2 min → 4 min …) and is written
    /// as its own linked run row. Only unattended scheduled runs are retried — a manual "trigger now" or a
    /// replay that fails is never retried (an operator is watching). <b>Effect-idempotency is the handler's
    /// responsibility:</b> a retried non-idempotent job repeats its side effects (same caveat as replay).
    /// A pending backoff retry is held in the RAM job store, so it does <b>not</b> survive a process restart.
    /// </summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// Whether a failure of this job raises an alert through <see cref="IJobFailureNotifier"/>. <b>On by
    /// default.</b> An alert fires only on a terminal failure an unattended owner needs to hear about — the
    /// <b>final</b> retry of a scheduled fire failing, or the job being silently misconfigured (its handler
    /// can't be resolved) — never on a transient failure that still has a retry to come, nor on a manual
    /// "trigger now" / replay failure (an operator is watching). Set to <c>false</c> to opt a noisy or
    /// self-healing job out of alerting; its failures are still recorded in the run log.
    /// </summary>
    public bool AlertOnFailure { get; init; } = true;

    /// <summary>Optional human-readable description for the dashboard.</summary>
    public string? Description { get; init; }
}