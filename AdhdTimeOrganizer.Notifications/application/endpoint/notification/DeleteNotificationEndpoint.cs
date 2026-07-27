using AdhdTimeOrganizer.Notifications.domain.entity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Notifications.application.endpoint.notification;

/// <summary>Hard-deletes one of the current user's notifications.</summary>
public class DeleteNotificationEndpoint(DbContext dbContext) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/notification/{id}");
        Roles(IEndpoint.GetUserRole());
        Summary(s => s.Summary = "Delete one of the current user's notifications");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<long>("id");
        var userId = User.GetId();

        // Scoped to the owner — prevents deleting another user's notification (IDOR).
        var notification = await dbContext.Set<Notification>()
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);

        if (notification is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        dbContext.Set<Notification>().Remove(notification);
        await dbContext.SaveChangesAsync(ct);

        await Send.NoContentAsync(ct);
    }
}