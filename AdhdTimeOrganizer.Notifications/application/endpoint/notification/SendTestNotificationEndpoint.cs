using FastEndpoints;
using MojaDigitalnaFirma.Kernel.notification;
using MojaDigitalnaFirma.Kernel.notification.payload;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Notifications.application.endpoint.notification;

/// <summary>
/// Dev/QA helper: sends a notification to the calling user so the full pipeline
/// (persist → SignalR → Web Push) can be verified end-to-end without a real business event.
/// Pass an optional <c>?type=</c> query param to live-test any notification card; a
/// representative sample payload is supplied so the rendered title/body match production.
/// Defaults to <see cref="NotificationType.Test"/>. Body-less so a plain POST works.
/// </summary>
public class SendTestNotificationEndpoint(INotificationService notificationService) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/notification/test");
        Roles(IEndpoint.GetUserRole());
        Summary(s => s.Summary = "Send a (configurable) test notification to the current user");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var type = Query<NotificationType?>("type", false) ?? NotificationType.Test;
        var userId = User.GetId();
        await notificationService.NotifyAsync(
            NotificationRecipients.User(userId),
            type,
            SamplePayloadFor(type),
            ct);

        await Send.NoContentAsync(ct);
    }

    /// <summary>
    /// Representative payloads matching <c>NotificationTextRenderer</c> for each type, so the live card
    /// renders the same title/body a real event would produce. Types whose body needs no payload return null.
    /// </summary>
    private static INotificationPayload? SamplePayloadFor(NotificationType type)
    {
        return type switch
        {
            NotificationType.DeadlineApproaching => new DeadlineApproachingPayload("Naplánovaná aktivita – blíži sa začiatok"),
            NotificationType.ReminderDigest => new ReminderDigestPayload(3),
            NotificationType.ScheduledJobFailed => new ScheduledJobFailedPayload("purge-expired-run-logs"),
            NotificationType.ScheduledJobOverdue => new ScheduledJobOverduePayload("reminder-scan"),
            NotificationType.Test => new TestNotificationPayload("Toto je testovacia notifikácia."),
            _ => null
        };
    }
}