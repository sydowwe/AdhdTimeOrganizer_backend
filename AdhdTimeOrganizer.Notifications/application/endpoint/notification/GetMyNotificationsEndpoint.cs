using AdhdTimeOrganizer.Notifications.application.dto;
using AdhdTimeOrganizer.Notifications.domain.entity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using MojaDigitalnaFirma.Kernel.notification;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Notifications.application.endpoint.notification;

/// <summary>Recent notifications for the current user (bell list).</summary>
public class GetMyNotificationsEndpoint(
    DbContext dbContext,
    INotificationTextRenderer renderer,
    INotificationPayloadEnricher payloadEnricher)
    : EndpointWithoutRequest<List<NotificationDto>>
{
    public override void Configure()
    {
        Get("/notification/mine");
        Roles(this.GetUserRole());
        Summary(s => s.Summary = "Get the current user's recent notifications");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();
        var beforeId = Query<long?>("beforeId", false);
        var limit = Math.Clamp(Query<int?>("limit", false) ?? 50, 1, 100);

        var query = dbContext.Set<Notification>().AsNoTracking()
            .Where(n => n.UserId == userId);

        // Ids are serial, so Id < beforeId is consistent with the CreatedTimestamp DESC, Id DESC
        // sort below — the next page is simply "older rows than the last one seen".
        if (beforeId.HasValue)
            query = query.Where(n => n.Id < beforeId.Value);

        var items = await query
            .OrderByDescending(n => n.CreatedTimestamp)
            .ThenByDescending(n => n.Id)
            .Take(limit)
            .ToListAsync(ct);

        // Resolve display names for the whole page in one read-seam round trip; the enriched JSON is only a
        // render input and is never written back (payload minimization — see INotificationPayloadEnricher).
        var payloads = await payloadEnricher.EnrichAsync(items.Select(n => n.PayloadJson).ToList(), ct);

        var result = items.Select((n, i) =>
        {
            var (title, body) = renderer.Render(n.Type, payloads[i]);
            return new NotificationDto(n.Id, n.Type, title, body, n.CreatedTimestamp, n.IsRead);
        }).ToList();

        await Send.OkAsync(result, ct);
    }
}