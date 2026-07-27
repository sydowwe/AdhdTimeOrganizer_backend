using AdhdTimeOrganizer.Notifications.domain.entity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Notifications.application.endpoint.quietHours;

/// <summary>
/// Clears the caller's quiet-hours window (back to the opt-in default: no quiet hours). Idempotent — 204
/// whether or not a row existed, so a UI toggle never has to know the current state. Self-scoped by
/// <c>User.GetId()</c>: there is no id in the route, so one user can never clear another's window.
/// <para>
/// Already-deferred notifications keep the <c>DeferredUntil</c> they were stamped with: the deferral decision
/// is frozen at send time (see <c>NotificationService</c>), so clearing the window affects future sends, not
/// deliveries already withheld.
/// </para>
/// </summary>
public class DeleteNotificationQuietHoursEndpoint(DbContext dbContext) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/notification-quiet-hours");
        Roles(IEndpoint.GetUserRole());
        Summary(s =>
        {
            s.Summary = "Clear my quiet-hours window";
            s.Response(204, "Quiet hours cleared (or none were set)");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();
        var existing = await dbContext.Set<NotificationQuietHours>()
            .FirstOrDefaultAsync(q => q.UserId == userId, ct);

        if (existing is not null)
        {
            dbContext.Remove(existing);
            await dbContext.SaveChangesAsync(ct);
        }

        await Send.NoContentAsync(ct);
    }
}