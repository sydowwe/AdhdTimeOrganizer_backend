using AdhdTimeOrganizer.Scheduler.domain.@enum;
using MojaDigitalnaFirma.Kernel.scheduling;
using Sydowwe.Framework.domain.audit;
using Sydowwe.Framework.domain.entity.@base;

namespace AdhdTimeOrganizer.Scheduler.domain.entity;

/// <summary>
/// Append-only run log — one INSERT per execution, written once at completion with
/// <see cref="StartedAt"/> + <see cref="FinishedAt"/> + a terminal <see cref="Outcome"/> together
/// (<see cref="RunOutcome"/> has no "Running" value). There is never an in-progress row that gets
/// updated, and a run row is never updated or deleted — corrections are new reversal/replay rows linked
/// via <see cref="ReplaysRunId"/>. A process crash mid-run therefore leaves no run row; recovery is the
/// next scheduled fire + the misfire policy, not a stuck row. In-flight visibility comes from
/// <see cref="ScheduledJob.Status"/> + the in-process concurrency gate, not from a run row.
/// <para>
/// Marked <see cref="NoAuditAttribute"/>: it is itself an append-only ledger (a self-audit), like the
/// Notification rows — auditing it would double-write. <b>Never put PII in <see cref="ErrorMessage"/></b>;
/// handlers own that expectation.
/// </para>
/// </summary>
[NoAudit]
public class ScheduledJobRun : BaseTableEntity
{
    public long ScheduledJobId { get; set; }
    public ScheduledJob ScheduledJob { get; set; } = null!;

    /// <summary>The keys at run time, snapshotted so the row survives a later rename/removal of the job.</summary>
    public required string JobKeySnapshot { get; set; }

    public required string HandlerKeySnapshot { get; set; }

    /// <summary>The fire this run was scheduled for. Null for a manual/replay fire (off-schedule).</summary>
    public DateTime? ScheduledFireTime { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    /// <summary>Stored computed column from started/finished — safe because both are written at insert.</summary>
    public int? DurationMs { get; set; }

    public RunOutcome Outcome { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorType { get; set; }

    public TriggerSource TriggerSource { get; set; }

    /// <summary>
    /// Auto-retry attempt number: <c>0</c> for the original (scheduled/manual/replay) fire, <c>1..MaxRetries</c>
    /// for each backoff retry of a failed scheduled fire. Together with the job's <c>MaxRetries</c> it tells the
    /// failure-alerting seam whether a <c>Failed</c> run was the <b>final</b> attempt (alert) or a transient one
    /// with a retry still to come (don't alert) — which cannot be recomputed cheaply from run rows alone.
    /// </summary>
    public int RetryAttempt { get; set; }

    /// <summary>Correlation id (from <c>Activity.Current?.TraceId</c>, as the audit interceptor uses).</summary>
    public required string CorrelationId { get; set; }

    /// <summary>
    /// The payload this run executed with (jsonb), so phase 04b's replay is faithful. A snapshot of
    /// <see cref="ScheduledJob.PayloadJson"/> and bound by the same payload PII contract — see that property,
    /// and <see cref="Kernel.notification.payload.INotificationPayload"/> for the rule itself. Surfaced by the
    /// run-detail endpoint, so anything written here is visible to an operator.
    /// </summary>
    public string? PayloadSnapshotJson { get; set; }

    /// <summary>Replay/correction lineage — points at the run this row re-ran or reversed.</summary>
    public long? ReplaysRunId { get; set; }

    public ScheduledJobRun? ReplaysRun { get; set; }
}