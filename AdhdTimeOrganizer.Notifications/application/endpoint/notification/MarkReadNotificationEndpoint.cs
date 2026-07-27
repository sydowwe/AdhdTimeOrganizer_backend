using AdhdTimeOrganizer.Notifications.domain.entity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Notifications.application.endpoint.notification;

/// <summary>Marks one of the current user's notifications as read.</summary>
public class MarkReadNotificationEndpoint(DbContext dbContext) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Patch("/notification/{id}/read");
        Roles(IEndpoint.GetUserRole());
        Summary(s => s.Summary = "Mark a notification as read");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<long>("id");
        var userId = User.GetId();

        // Scoped to the owner — prevents reading/altering another user's notification (IDOR).
        var notification = await dbContext.Set<Notification>()
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);

        if (notification is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            try
            {
                await dbContext.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Concurrent mark-as-read — idempotent, so the race winner already did the work.
            }
        }

        await Send.NoContentAsync(ct);
    }
}