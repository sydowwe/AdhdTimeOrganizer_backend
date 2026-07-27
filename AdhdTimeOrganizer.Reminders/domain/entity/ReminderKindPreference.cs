using MojaDigitalnaFirma.Kernel.notification;
using Sydowwe.Framework.domain.audit;
using Sydowwe.Framework.domain.entity.@base;

namespace AdhdTimeOrganizer.Reminders.domain.entity;

/// <summary>
/// A user's per-reminder-<b>kind</b> opt-out — keyed by <c>(UserId, OwnerModule, Kind)</c>. The scheduling-side
/// preference layer (phase 04a) that the Notification module has no concept of: it lets a user mute one kind of
/// scheduled reminder (e.g. "probation-ending reminders") <b>without</b> muting all push/in-app delivery.
/// <para>
/// <b>Absence-means-enabled</b> (matching the Notification module's <c>NotificationPreference</c> convention):
/// only an explicit row with <see cref="Enabled"/> = <c>false</c> suppresses the kind. The scanner (03a) consults
/// these rows at resolve-recipients time and drops opted-out recipients; if all recipients opted out it logs a
/// <c>Skipped</c> / <c>OptedOut</c> dispatch row and sends nothing.
/// </para>
/// <para>
/// <b>Boundary vs the Notification module:</b> this module decides <i>whether</i> a scheduled reminder of a kind
/// may fire for a user; the Notification module still owns the final channel transport and its own per-channel
/// <c>NotificationPreference</c> filtering. <see cref="ChannelHint"/> is therefore <b>advisory only</b> in v1 —
/// <c>INotificationService.NotifyAsync</c> carries no channel selector, so this module cannot enforce a channel
/// choice; it is stored as metadata and the Notification module owns routing. The opt-out itself <i>is</i> enforced.
/// </para>
/// <para>
/// Mirrors the module's domain-free, plain-table convention: <see cref="BaseTableEntity"/> with a plain indexed
/// <see cref="UserId"/> (no FK to the user table). <c>[NoAudit]</c> mirrors <c>NotificationPreference</c> — a
/// high-churn user toggle, not audit-worthy.
/// </para>
/// </summary>
[NoAudit]
public class ReminderKindPreference : BaseTableEntity
{
    public long UserId { get; set; }

    /// <summary>The owning module of the reminder kind (matches <c>ReminderKey.OwnerModule</c> / <c>ReminderDefinition.OwnerModule</c>).</summary>
    public required string OwnerModule { get; set; }

    /// <summary>The reminder kind (matches <c>ReminderKey.Kind</c> / <c>ReminderDefinition.Kind</c>).</summary>
    public required string Kind { get; set; }

    /// <summary>Absence-means-enabled: a row exists only to record an opt-out (<c>false</c>) — or a later opt-in (<c>true</c>).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Advisory preferred channel for this kind. NOT enforced here (the Notification contract has no channel selector).</summary>
    public NotificationChannel? ChannelHint { get; set; }
}