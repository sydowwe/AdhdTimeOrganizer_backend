using Sydowwe.Framework.Contracts.notification;

namespace AdhdTimeOrganizer.Reminders.application.dto.preference;

/// <summary>
/// One per-kind opt-out row in a user's preferences. <c>UserId</c> is omitted — the read is always the caller's
/// own rows. <see cref="ChannelHint"/> is advisory only (the Notification contract owns channel routing).
/// </summary>
public record ReminderKindPreferenceDto
{
    public required string OwnerModule { get; init; }
    public required string Kind { get; init; }
    public required bool Enabled { get; init; }
    public NotificationChannel? ChannelHint { get; init; }
}