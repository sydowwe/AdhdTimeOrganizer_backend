using AdhdTimeOrganizer.Notifications.application.dto;
using AdhdTimeOrganizer.Notifications.domain.entity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Notifications.application.endpoint.notification;

/// <summary>Unread count for the current user's bell — uncapped, unlike the 50-row history page.</summary>
public class GetUnreadNotificationCountEndpoint(DbContext dbContext)
    : EndpointWithoutRequest<UnreadNotificationCountResponse>
{
    public override void Configure()
    {
        Get("/notification/unread-count");
        Roles(this.GetUserRole());
        Summary(s => s.Summary = "Get the current user's unread notification count");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();
        var count = await dbContext.Set<Notification>().AsNoTracking()
            .CountAsync(n => n.UserId == userId && !n.IsRead, ct);

        await Send.OkAsync(new UnreadNotificationCountResponse(count), ct);
    }
}