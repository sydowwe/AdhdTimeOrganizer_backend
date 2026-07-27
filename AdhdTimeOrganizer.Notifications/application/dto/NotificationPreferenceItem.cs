using MojaDigitalnaFirma.Kernel.notification;

namespace AdhdTimeOrganizer.Notifications.application.dto;

/// <summary>
/// One (type, channel) row in the full preference matrix. <see cref="Enabled"/> is the
/// *effective* state — an explicit <c>NotificationPreference</c> row if one exists, else the
/// channel's per-type default (<see cref="application.NotificationChannelDefaults"/>).
/// </summary>
public record NotificationPreferenceItem(NotificationType Type, NotificationChannel Channel, bool Enabled);