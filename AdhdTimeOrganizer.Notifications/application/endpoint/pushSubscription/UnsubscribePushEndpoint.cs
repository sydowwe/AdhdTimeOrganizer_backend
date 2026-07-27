using AdhdTimeOrganizer.Notifications.application.dto;
using AdhdTimeOrganizer.Notifications.domain.entity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Notifications.application.endpoint.pushSubscription;

/// <summary>Removes a Web Push subscription (e.g. user revoked permission / signed out).</summary>
public class UnsubscribePushEndpoint(DbContext dbContext) : Endpoint<UnsubscribePushRequest>
{
    public override void Configure()
    {
        Post("/push-subscription/unsubscribe");
        Roles(IEndpoint.GetUserRole());
        Summary(s => s.Summary = "Remove the current user's Web Push subscription");
    }

    public override async Task HandleAsync(UnsubscribePushRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        var sub = await dbContext.Set<PushSubscription>()
            .FirstOrDefaultAsync(x => x.Endpoint == req.Endpoint && x.UserId == userId, ct);

        if (sub is not null)
        {
            dbContext.Set<PushSubscription>().Remove(sub);
            try
            {
                await dbContext.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Concurrent unsubscribe — the other request already deleted the row.
            }
        }

        await Send.NoContentAsync(ct);
    }
}