using AdhdTimeOrganizer.Notifications.application.dto;
using AdhdTimeOrganizer.Notifications.domain.entity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Notifications.application.endpoint.quietHours;

/// <summary>
/// The caller's own quiet-hours window. Strictly self-scoped: it only ever reads the row for
/// <c>User.GetId()</c>, so there is no cross-user read path.
/// </summary>
public class GetMyNotificationQuietHoursEndpoint(DbContext dbContext)
    : EndpointWithoutRequest<MyNotificationQuietHoursResponse>
{
    public override void Configure()
    {
        Get("/notification-quiet-hours");
        Roles(this.GetUserRole());
        Summary(s => s.Summary = "Get my quiet-hours window (null when none is set)");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();

        var window = await dbContext.Set<NotificationQuietHours>().AsNoTracking()
            .Where(q => q.UserId == userId)
            .Select(q => new NotificationQuietHoursDto { StartMinute = q.StartMinute, EndMinute = q.EndMinute })
            .FirstOrDefaultAsync(ct);

        await Send.OkAsync(new MyNotificationQuietHoursResponse { QuietHours = window }, ct);
    }
}