using AdhdTimeOrganizer.Notifications.domain.entity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Notifications.application.endpoint.notification;

/// <summary>
/// Marks all of the current user's unread notifications as read. <c>Notification</c> is
/// [NoAudit], so the bulk <c>ExecuteUpdateAsync</c> (which bypasses the ChangeTracker and thus
/// the audit interceptor) is acceptable here — see CLAUDE.md's auditing "known limitation".
/// </summary>
public class MarkAllNotificationsReadEndpoint(DbContext dbContext) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/notification/read-all");
        Roles(this.GetUserRole());
        Summary(s => s.Summary = "Mark all of the current user's notifications as read");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();
        var now = DateTime.UtcNow;

        await dbContext.Set<Notification>()
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, now), ct);

        await Send.NoContentAsync(ct);
    }
}