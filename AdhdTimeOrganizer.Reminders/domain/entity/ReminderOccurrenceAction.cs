using AdhdTimeOrganizer.Reminders.domain.@enum;
using Sydowwe.Framework.domain.audit;
using Sydowwe.Framework.domain.entity.@base;

namespace AdhdTimeOrganizer.Reminders.domain.entity;

/// <summary>
/// A recipient's per-occurrence <b>snooze / dismiss</b> action (phase 05b) — recorded
/// <b>per <c>(ReminderDefinition, OccurrenceAt, UserId)</c></b> in this append-only ledger, never by
/// mutating the definition's <b>shared</b> <see cref="ReminderDefinition.NextOccurrenceAt"/> (which other
/// recipients of the same occurrence depend on) or any past <see cref="ReminderDispatch"/> row.
/// <para>
/// <b>Why per recipient.</b> A reminder can have many recipients but only one shared
/// <c>NextOccurrenceAt</c>. So one recipient deferring/suppressing their own delivery of an occurrence must
/// not move the schedule for everyone — it is captured here and consulted at the scanner's resolve-recipients
/// step. <b>Dismiss</b> drops the caller from that occurrence's send; <b>Snooze</b> drops them now and the
/// snoozed <c>(occurrence, recipient, SnoozeUntil)</c> becomes its own due item the scanner re-evaluates at
/// <see cref="SnoozeUntil"/> (a separate "due snoozes" source, since the definition scan has already advanced
/// past the original occurrence).
/// </para>
/// <para>
/// <b>Append-only, like <see cref="ReminderDispatch"/>:</b> rows are inserted, never updated/deleted; a
/// correction is a new <see cref="ReminderActionType.Reversal"/> row linked via <see cref="ReversesActionId"/>.
/// The effective state of a <c>(definition, occurrence, user)</c> is the latest non-reversed action. A snooze
/// re-delivery is deduped by <b>the marker's own state</b> (not the occurrence-keyed dispatch dedup, because
/// the original occurrence may already carry a <c>Sent</c> row): it is considered delivered once a
/// <see cref="ReminderDispatch"/> references it via <see cref="ReminderDispatch.ReminderOccurrenceActionId"/>.
/// </para>
/// <para>
/// Mirrors the module's domain-free, plain-table convention: <see cref="BaseTableEntity"/> with a plain
/// <see cref="UserId"/> (no FK to the user table) and <c>[NoAudit]</c> (an append-only ledger is
/// self-auditing, like <see cref="ReminderDispatch"/>). FKs are <b>Restrict</b> so history is never
/// cascade-erased with a definition.
/// </para>
/// </summary>
[NoAudit]
public class ReminderOccurrenceAction : BaseTableEntity
{
    public long ReminderDefinitionId { get; set; }
    public ReminderDefinition ReminderDefinition { get; set; } = null!;

    /// <summary>The occurrence instant this action is for — part of the per-recipient key with <see cref="UserId"/>.</summary>
    public DateTimeOffset OccurrenceAt { get; set; }

    /// <summary>The recipient who acted. Plain indexed column — no FK to the user table (validated by the caller being a recipient).</summary>
    public long UserId { get; set; }

    public ReminderActionType ActionType { get; set; }

    /// <summary>When a <see cref="ReminderActionType.Snooze"/> defers delivery to; null for dismiss / reversal.</summary>
    public DateTimeOffset? SnoozeUntil { get; set; }

    /// <summary>When the recipient performed the action (used to pick the latest effective action per key).</summary>
    public DateTimeOffset ActedAt { get; set; }

    /// <summary>Reversal lineage — the action this row reverses. Corrections are reversal rows, never updates/deletes.</summary>
    public long? ReversesActionId { get; set; }

    public ReminderOccurrenceAction? ReversesAction { get; set; }
}