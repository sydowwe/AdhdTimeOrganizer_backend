using AdhdTimeOrganizer.Reminders.domain.@enum;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Contracts.reminders;
using Sydowwe.Framework.domain.audit;
using Sydowwe.Framework.domain.entity.@base;
using Sydowwe.Framework.domain.entityInterface;

namespace AdhdTimeOrganizer.Reminders.domain.entity;

/// <summary>
/// The registered reminder — one row per idempotency key
/// (<see cref="OwnerModule"/>, <see cref="SubjectType"/>, <see cref="SubjectId"/>, <see cref="Kind"/>),
/// enforced by a unique composite index. <c>IReminderRegistry.RegisterAsync</c> (phase 02) upserts by that
/// key, so re-registering updates in place and never duplicates. Domain-free by construction: the key
/// columns are strings, content is a <see cref="TemplateKey"/> + opaque <see cref="PayloadJson"/>, and
/// recipients are explicit user ids (<see cref="Recipients"/>) or a <see cref="RecipientResolverKey"/> —
/// there is no FK to any domain table and no <c>using AdhdTimeOrganizer.&lt;Domain&gt;</c>.
/// <para>
/// Module-owned infrastructure, NOT user-scoped: derives from <see cref="BaseTableEntity"/> (not
/// <c>BaseEntityWithUser</c>) so a background registrar with no authenticated user can write it without the
/// <c>UserId == 0</c> FK trap — mirrors the Scheduler registry. Per-reminder timing lives here in
/// <see cref="NextOccurrenceAt"/> (the scanner's source of truth), never as Quartz triggers.
/// </para>
/// </summary>
public class ReminderDefinition : BaseTableEntity, ISoftDeletable
{
    // --- Idempotency key (unique composite index — see ReminderDefinitionEntityConfiguration). ---
    public required string OwnerModule { get; set; }
    public required string SubjectType { get; set; }

    /// <summary>The subject entity's id as a string (matches <c>ReminderKey.SubjectId</c>).</summary>
    public required string SubjectId { get; set; }

    public required string Kind { get; set; }

    // --- Content (self-contained so dispatch needs nothing else). ---

    /// <summary>Stable content key; resolves to text via <see cref="NotificationType"/> or a keyed renderer (phase 03).</summary>
    public required string TemplateKey { get; set; }

    /// <summary>
    /// The registration's <c>Payload</c> serialised verbatim (jsonb). Schedule/recipients/key are their own columns, not part of this blob.
    /// <para>
    /// Bound by the payload PII contract — <b>the rule and the reasoning live on
    /// <see cref="INotificationPayload"/></b>, stated once for
    /// all three modules that persist a payload. This is no longer a convention: <c>ReminderRegistration.Payload</c>
    /// is typed <see cref="IReminderPayload"/>, so module code cannot register free text, and the one path that
    /// still accepts a hand-written document (the manual-ops register endpoint) rejects person-data keys at
    /// runtime with a 400.
    /// </para>
    /// <para>
    /// <see cref="AuditIgnoreAttribute"/> stays regardless: the entity remains audited (registry edits to
    /// schedule/recipients/status are captured), but an owner-supplied blob does not belong in <c>audit_log</c>
    /// snapshots, which would retain it past GDPR erasure (Art.5/Art.32) — the same concern the logging
    /// redaction rules guard against. See review AUDIT-2 / SEC-1.
    /// </para>
    /// </summary>
    [AuditIgnore]
    public string PayloadJson { get; set; } = "{}";

    /// <summary>The mapped Notification type rendered from <see cref="PayloadJson"/>; null when a custom renderer supplies the text.</summary>
    public NotificationType? NotificationType { get; set; }

    // --- Schedule (per-type fields nullable; only meaningful for the matching ScheduleType). ---
    public ReminderScheduleType ScheduleType { get; set; }

    /// <summary>One-shot deadline. Set only for <see cref="ReminderScheduleType.OneShot"/>. Each <see cref="LeadOffsets"/> entry produces an occurrence relative to this.</summary>
    public DateTimeOffset? DueAt { get; set; }

    /// <summary>Recurrence cadence. Set only for <see cref="ReminderScheduleType.RecurringInterval"/>.</summary>
    public ReminderIntervalPreset? IntervalPreset { get; set; }

    /// <summary>Cron expression (UTC). Set only for <see cref="ReminderScheduleType.RecurringCron"/>.</summary>
    public string? Cron { get; set; }

    /// <summary>Date the interval recurrence is anchored to. Set only for <see cref="ReminderScheduleType.RecurringInterval"/>.</summary>
    public DateTimeOffset? AnchorDate { get; set; }

    /// <summary>Optional end of a recurring schedule (no occurrences after it). Recurring types only.</summary>
    public DateTimeOffset? EndsAt { get; set; }

    // --- Recipients. ---
    public RecipientMode RecipientMode { get; set; }

    /// <summary>The resolver strategy key. Set only when <see cref="RecipientMode"/> is <see cref="RecipientMode.ResolverStrategy"/>.</summary>
    public string? RecipientResolverKey { get; set; }

    /// <summary>Explicit recipients. Populated only when <see cref="RecipientMode"/> is <see cref="RecipientMode.ExplicitUsers"/>.</summary>
    public ICollection<ReminderRecipient> Recipients { get; set; } = new List<ReminderRecipient>();

    /// <summary>Lead-time offsets for one-shot deadlines; each produces its own occurrence instant. One-shot only.</summary>
    public ICollection<ReminderLeadOffset> LeadOffsets { get; set; } = new List<ReminderLeadOffset>();

    // --- Scheduler state (read/written by the scanner — never Quartz triggers). ---
    public ReminderStatus Status { get; set; } = ReminderStatus.Active;

    /// <summary>The soonest not-yet-dispatched occurrence. The scan in phase 03 seeks <c>Status = Active AND NextOccurrenceAt &lt;= now</c>.</summary>
    public DateTimeOffset? NextOccurrenceAt { get; set; }

    /// <summary>The most recently dispatched occurrence instant.</summary>
    public DateTimeOffset? LastOccurrenceAt { get; set; }

    /// <summary>Set when a one-shot's offsets are all dispatched or a recurring reminder passes <see cref="EndsAt"/>.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    // --- Phase-04 placeholders (populated in 04; carried straight from ReminderRegistration). ---

    /// <summary>Digest-window key: occurrences sharing a key aggregate into one notification (phase 04b).</summary>
    public string? DigestKey { get; set; }

    /// <summary>Preferred delivery channel hint (phase 04a).</summary>
    public NotificationChannel? ChannelHint { get; set; }

    public bool IsActive { get; set; } = true;
}