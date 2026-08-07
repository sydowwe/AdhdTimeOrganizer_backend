using Sydowwe.Framework.Contracts.notification;

namespace AdhdTimeOrganizer.Reminders.application.dto.preference;

/// <summary>
/// Upsert the caller's opt-out for one reminder kind, keyed by <c>(OwnerModule, Kind)</c> (the user is the
/// authenticated caller). Absence-means-enabled, so a row with <see cref="Enabled"/> = <c>true</c> is the explicit
/// opt-in that reverses a prior opt-out.
/// </summary>
public record UpdateReminderKindPreferenceRequest
{
    public required string OwnerModule { get; init; }
    public required string Kind { get; init; }
    public required bool Enabled { get; init; }

    /// <summary>Advisory preferred channel for this kind. Stored as metadata; NOT enforced (the Notification module owns channel routing).</summary>
    public NotificationChannel? ChannelHint { get; init; }
}