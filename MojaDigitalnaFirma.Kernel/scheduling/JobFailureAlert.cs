namespace MojaDigitalnaFirma.Kernel.scheduling;

/// <summary>
/// The <b>PII-free</b> payload of a job alert (see <see cref="IJobFailureNotifier"/>). Carries only the
/// technical identifiers an operator needs to locate the problem in the scheduler dashboard: the job's key +
/// owning module, the terminal error <b>type</b> (never the verbatim error message — handlers own its hygiene
/// by convention only), the run-log row id, and when it was observed. No person names / free text — same
/// discipline as <see cref="RecurringJobRegistration.Payload"/>.
/// <para>
/// <b>Two kinds, one record</b> (<see cref="Kind"/>) — a job that ran and failed, and a job that never fired
/// at all. Follow-up 08 deliberately widened this record rather than inventing a parallel seam: the routing,
/// the recipient set, the throttling and the PII discipline are identical, and only the fields below differ.
/// The widening is why <see cref="RunId"/> is nullable — an overdue job has no run row <i>by definition</i>.
/// </para>
/// </summary>
public sealed record JobFailureAlert
{
    /// <summary>Which failure mode this is. Defaults to <see cref="JobAlertKind.TerminalFailure"/> — the dispatcher's case.</summary>
    public JobAlertKind Kind { get; init; } = JobAlertKind.TerminalFailure;

    /// <summary>Stable business key of the job (its idempotency key + dashboard identity).</summary>
    public required string JobKey { get; init; }

    /// <summary>Owning-module label, for routing/labelling the alert (e.g. "Attendance").</summary>
    public required string OwnerModule { get; init; }

    /// <summary>
    /// The terminal failure's error <b>type</b> (an exception type name, or a marker like "HandlerNotFound").
    /// Never the raw message. For <see cref="JobAlertKind.Overdue"/> there is no exception, so the sweep sets
    /// the marker <c>"Overdue"</c>.
    /// </summary>
    public string? ErrorType { get; init; }

    /// <summary>
    /// Id of the append-only <c>scheduled_job_run</c> row recording this failure (a deep-link target for the
    /// dashboard). <b>Null for <see cref="JobAlertKind.Overdue"/></b>: the whole point of that kind is that no
    /// run row exists, so a consumer rendering a deep link must degrade to a job-key link.
    /// </summary>
    public long? RunId { get; init; }

    /// <summary>
    /// When the producer <i>observed</i> the condition (UTC): the instant the failing run finished, or the
    /// instant the overdue sweep noticed the miss. Deliberately not named after either one — the two kinds
    /// share this field and "detected at" is honest for both.
    /// </summary>
    public required DateTime DetectedAtUtc { get; init; }

    /// <summary>
    /// For <see cref="JobAlertKind.Overdue"/>: the fire the job <b>was expected to make and didn't</b>
    /// (its stale <c>NextRunAt</c>) — the number that tells an operator how long it has been silent. Null for
    /// a terminal failure, which has no such expectation.
    /// </summary>
    public DateTime? ExpectedRunAtUtc { get; init; }
}