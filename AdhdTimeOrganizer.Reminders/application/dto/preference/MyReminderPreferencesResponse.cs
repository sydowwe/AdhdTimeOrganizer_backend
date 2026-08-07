namespace AdhdTimeOrganizer.Reminders.application.dto.preference;

/// <summary>
/// The caller's reminder dispatch-policy preferences: per-kind opt-outs.
/// <para>
/// <b>Quiet hours are no longer here.</b> There is one window per user for the whole deployment and the
/// Notifications module owns it (notifications follow-up 05) — read and write it at
/// <c>GET|PUT|DELETE /notification-quiet-hours</c>. This module still *honours* it (the scan defers a due
/// occurrence when every recipient is inside their window), reading it through the <c>Sydowwe.Framework.Contracts</c>
/// <c>IQuietHoursReader</c> seam.
/// </para>
/// </summary>
public record MyReminderPreferencesResponse
{
    public required IReadOnlyList<ReminderKindPreferenceDto> KindPreferences { get; init; }
}