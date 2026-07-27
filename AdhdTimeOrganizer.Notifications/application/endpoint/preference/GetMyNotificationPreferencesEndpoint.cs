using AdhdTimeOrganizer.Notifications.application.dto;
using AdhdTimeOrganizer.Notifications.domain.entity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using MojaDigitalnaFirma.Kernel.notification;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Notifications.application.endpoint.preference;

/// <summary>
/// The full (type, channel) preference matrix for the current user — one entry per enum pair,
/// doubling as the catalog. <c>Enabled</c> is the effective state: an explicit row if one
/// exists, else <see cref="NotificationChannelDefaults.IsEnabledByDefault"/> — the same rule the
/// dispatcher applies, so this view never disagrees with what actually gets sent.
/// </summary>
public class GetMyNotificationPreferencesEndpoint(DbContext dbContext)
    : EndpointWithoutRequest<List<NotificationPreferenceItem>>
{
    public override void Configure()
    {
        Get("/notification-preference/mine");
        Roles(IEndpoint.GetUserRole());
        Summary(s => s.Summary = "Get the current user's effective notification preference matrix");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();
        var prefs = await dbContext.Set<NotificationPreference>().AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToListAsync(ct);

        var result = new List<NotificationPreferenceItem>();
        foreach (var type in Enum.GetValues<NotificationType>())
        foreach (var channel in Enum.GetValues<NotificationChannel>())
        {
            var pref = prefs.FirstOrDefault(p => p.Type == type && p.Channel == channel);
            var enabled = pref?.Enabled ?? NotificationChannelDefaults.IsEnabledByDefault(type, channel);
            result.Add(new NotificationPreferenceItem(type, channel, enabled));
        }

        await Send.OkAsync(result, ct);
    }
}