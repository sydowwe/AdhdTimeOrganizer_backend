using AdhdTimeOrganizer.Reminders.domain.@enum;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.domain.audit;
using Sydowwe.Framework.domain.entity.@base;

namespace AdhdTimeOrganizer.Reminders.domain.entity;

/// <summary>
/// The append-only dispatch ledger — one row per <c>(ReminderDefinition, occurrence instant)</c> the
/// scanner (phase 03) actually acted on. Mirrors the Inventory <c>StockMovement</c> / Scheduler
/// <c>ScheduledJobRun</c> ledgers: rows are <b>inserted, never updated or deleted</b>; a correction is a
/// new <see cref="DispatchOutcome.Reversed"/> row linked via <see cref="ReversesDispatchId"/>. The scanner
/// checks this log (the <c>(ReminderDefinitionId, OccurrenceAt)</c> index) before sending, so a re-run /
/// misfire / overlap never double-sends.
/// <para>
/// Marked <see cref="NoAuditAttribute"/>: an append-only ledger is self-auditing, like
/// <c>ScheduledJobRun</c> and the Notification rows — auditing it would double-write. The
/// <see cref="RecipientsSnapshot"/> records who the notification went to; <b>never put a person's name or
/// other free-text PII in it</b> — store user ids only.
/// </para>
/// </summary>
[NoAudit]
public class ReminderDispatch : BaseTableEntity
{
    public long ReminderDefinitionId { get; set; }
    public ReminderDefinition ReminderDefinition { get; set; } = null!;

    /// <summary>The scheduled instant this row is for — the dedup coordinate paired with <see cref="ReminderDefinitionId"/>.</summary>
    public DateTimeOffset OccurrenceAt { get; set; }

    /// <summary>When the dispatch was actually acted on.</summary>
    public DateTimeOffset DispatchedAt { get; set; }

    public DispatchOutcome Outcome { get; set; }

    /// <summary>Why a <see cref="DispatchOutcome.Skipped"/> row was not sent; null otherwise.</summary>
    public SkipReason? SkipReason { get; set; }

    /// <summary>The Notification type this dispatch resolved to (snapshotted so the row survives a later registration change).</summary>
    public NotificationType? NotificationTypeSnapshot { get; set; }

    /// <summary>The reminder's template key at dispatch time.</summary>
    public string? TemplateKeySnapshot { get; set; }

    /// <summary>Who it actually went to (jsonb, user ids only — no PII).</summary>
    public string RecipientsSnapshot { get; set; } = "[]";

    /// <summary>Correlation id, from <c>Activity.Current?.TraceId</c> (as the audit interceptor uses).</summary>
    public required string CorrelationId { get; set; }

    /// <summary>Reversal lineage — the dispatch row this one reverses. Corrections are reversal rows, never updates/deletes.</summary>
    public long? ReversesDispatchId { get; set; }

    public ReminderDispatch? ReversesDispatch { get; set; }

    /// <summary>
    /// The snooze action (phase 05b) this dispatch fulfils, if any. Set <b>only</b> on a snoozed re-delivery
    /// (the "due snoozes" second source) — it is the <b>marker-state dedup</b> link: a snooze is considered
    /// delivered once a dispatch references it, so the original occurrence's existing <c>Sent</c> row never
    /// suppresses the re-delivery and the re-delivery never fires twice. Null for ordinary occurrence dispatches.
    /// </summary>
    public long? ReminderOccurrenceActionId { get; set; }

    public ReminderOccurrenceAction? ReminderOccurrenceAction { get; set; }
}