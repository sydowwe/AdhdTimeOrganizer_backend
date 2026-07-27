using AdhdTimeOrganizer.Scheduler.domain.@enum;
using MojaDigitalnaFirma.Kernel.scheduling;
using Sydowwe.Framework.domain.audit;
using Sydowwe.Framework.domain.entity.@base;
using Sydowwe.Framework.domain.entityInterface;

namespace AdhdTimeOrganizer.Scheduler.domain.entity;

/// <summary>
/// The job registry — one row per <see cref="JobKey"/>. The unique <see cref="JobKey"/> index is the
/// idempotency guarantee behind <see cref="IScheduler.RegisterRecurringJobAsync"/>. Module-owned infra
/// (not user-scoped): derives from the plain table base so background registration with no
/// authenticated user is safe. The schedule discriminator + interval/cron fields mirror
/// <see cref="ScheduleSpec"/>. Registry edits (pause/resume/reschedule) stay audited; the per-run
/// observability columns are <see cref="AuditIgnoreAttribute"/> since they are rewritten on every fire
/// (the run log already captures each run). <see cref="PayloadJson"/> is also excluded: it is opaque,
/// convention-only PII-free (see <see cref="Kernel.scheduling.RecurringJobRegistration.Payload"/>), and
/// free-text PII in it would not be caught by the audit redactor and would outlive GDPR erasure.
/// </summary>
public class ScheduledJob : BaseTableEntity, ISoftDeletable
{
    public required string JobKey { get; set; }
    public required string HandlerKey { get; set; }
    public required string OwnerModule { get; set; }
    public string? Description { get; set; }

    // Schedule (mirrors ScheduleSpec).
    public JobScheduleType ScheduleType { get; set; }
    public string? Cron { get; set; }
    public JobIntervalPreset? IntervalPreset { get; set; }
    public int? IntervalCount { get; set; }

    /// <summary>The fixed UTC instant a one-shot fires at (mirrors <see cref="ScheduleSpec.RunAtUtc"/>); null unless <see cref="ScheduleType"/> is <see cref="JobScheduleType.Once"/>.</summary>
    public DateTime? RunAtUtc { get; set; }

    /// <summary>IANA zone the wall-clock schedule is anchored to (mirrors <see cref="ScheduleSpec.TimeZoneId"/>); null ⇒ UTC.</summary>
    public string? TimeZoneId { get; set; }

    public MisfirePolicy MisfirePolicy { get; set; }
    public bool DisallowConcurrent { get; set; }

    /// <summary>Max auto-retries of a failed scheduled fire (mirrors <see cref="Kernel.scheduling.RecurringJobRegistration.MaxRetries"/>). Default 3; 0 disables retries.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Whether a terminal failure raises an alert (mirrors <see cref="Kernel.scheduling.RecurringJobRegistration.AlertOnFailure"/>). Default true; false opts the job out of failure alerting.</summary>
    public bool AlertOnFailure { get; set; } = true;

    /// <summary>
    /// Opaque payload (jsonb), stored verbatim so the dispatcher is self-contained and a replay can rehydrate it.
    /// <para>
    /// Bound by the payload PII contract — <b>the rule and the reasoning live on
    /// <see cref="Kernel.notification.payload.INotificationPayload"/></b>, stated once for all three modules
    /// that persist a payload. Scheduler is the <i>lower-risk</i> of the three and is enforced differently, on
    /// purpose: this column is handler <b>configuration</b> ("purge older than X"), not per-subject content,
    /// which is why the scheduler review rated it MEDIUM rather than HIGH. Job payloads are therefore <b>not</b>
    /// typed against a marker — the rule here is a convention plus <see cref="AuditIgnoreAttribute"/>, and
    /// typing them is a recorded residual rather than something this column pretends to guarantee.
    /// </para>
    /// </summary>
    [AuditIgnore]
    public string? PayloadJson { get; set; }

    // Observability state — written by the registrar/dispatcher, NOT the source of Quartz timing.
    public JobStatus Status { get; set; } = JobStatus.Active;

    [AuditIgnore]
    public DateTime? NextRunAt { get; set; }

    [AuditIgnore]
    public DateTime? LastRunAt { get; set; }

    [AuditIgnore]
    public RunOutcome? LastOutcome { get; set; }

    /// <summary>
    /// When the currently-executing run started (UTC), or <c>null</c> when no run is in flight. Written by
    /// <c>ScheduledJobDispatcher</c> immediately <b>before</b> it invokes the handler and cleared when the run
    /// completes — the one registry field that is deliberately observable <i>during</i> a run.
    /// <para>
    /// <b>Why it exists (scheduler follow-up 08).</b> <see cref="NextRunAt"/> is only recomputed <i>after</i> a
    /// handler returns, so a job whose body takes minutes holds a stale, already-past <see cref="NextRunAt"/>
    /// for its entire execution and looks indistinguishable from a job that never fired. Without this marker
    /// the overdue sweep would alert on jobs that are running correctly, and the only defence was a wide
    /// guess-margin. This makes the distinction <b>exact</b>: in-flight is a fact the dispatcher records, not a
    /// duration the sweep infers.
    /// </para>
    /// <para>
    /// <b>Not authoritative for concurrency</b> — <c>IJobConcurrencyGate</c> owns <c>DisallowConcurrent</c>
    /// enforcement. This is observability, like its neighbours, and is treated as advisory: a marker left
    /// behind by a process that died mid-run self-heals via the sweep's staleness bound
    /// (<c>OverdueJobSweepOptions.MaxRunHours</c>), so a crash can never shield a job from alerting forever.
    /// </para>
    /// </summary>
    [AuditIgnore]
    public DateTime? RunningSince { get; set; }

    public bool IsActive { get; set; } = true;
}