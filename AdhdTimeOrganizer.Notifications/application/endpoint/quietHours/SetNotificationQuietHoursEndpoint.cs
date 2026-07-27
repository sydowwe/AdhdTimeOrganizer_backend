using AdhdTimeOrganizer.Notifications.application.dto;
using AdhdTimeOrganizer.Notifications.domain.entity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using MojaDigitalnaFirma.Kernel.notification;
using Npgsql;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Notifications.application.endpoint.quietHours;

/// <summary>
/// Upserts the caller's quiet-hours window. Self-scoped by <c>User.GetId()</c> — the request carries no user
/// id, so there is no cross-user write path. Minutes are 0..1439 local-to-deployment; <c>Start &gt; End</c> is
/// a valid overnight window, <c>Start == End</c> is rejected (clear the window with <c>DELETE</c> instead of
/// encoding an ambiguous zero/all-day span).
/// </summary>
public class SetNotificationQuietHoursEndpoint(DbContext dbContext) : Endpoint<SetNotificationQuietHoursRequest>
{
    public override void Configure()
    {
        Put("/notification-quiet-hours");
        Roles(IEndpoint.GetUserRole());
        Summary(s =>
        {
            s.Summary = "Set my quiet-hours window";
            s.Description = "During the window Web Push and Email are deferred until it ends; the in-app bell is unaffected.";
            s.Response(204, "Quiet hours set");
            s.Response(400, "Invalid window");
        });
    }

    public override async Task HandleAsync(SetNotificationQuietHoursRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        if (req.StartMinute is < 0 or >= QuietHoursPolicy.MinutesPerDay || req.EndMinute is < 0 or >= QuietHoursPolicy.MinutesPerDay)
            AddError("StartMinute and EndMinute must be between 0 and 1439.");
        if (req.StartMinute == req.EndMinute)
            AddError("StartMinute and EndMinute must differ — to disable quiet hours, delete them.");

        if (ValidationFailed)
        {
            await Send.ErrorsAsync(400, ct);
            return;
        }

        var existing = await dbContext.Set<NotificationQuietHours>()
            .FirstOrDefaultAsync(q => q.UserId == userId, ct);

        if (existing is null)
        {
            dbContext.Add(new NotificationQuietHours
            {
                UserId = userId,
                StartMinute = req.StartMinute,
                EndMinute = req.EndMinute
            });

            try
            {
                await dbContext.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Concurrent insert won the race — detach and update the winning row.
                dbContext.ChangeTracker.Clear();
                existing = await dbContext.Set<NotificationQuietHours>().FirstAsync(q => q.UserId == userId, ct);
                existing.StartMinute = req.StartMinute;
                existing.EndMinute = req.EndMinute;
                await dbContext.SaveChangesAsync(ct);
            }
        }
        else
        {
            existing.StartMinute = req.StartMinute;
            existing.EndMinute = req.EndMinute;
            await dbContext.SaveChangesAsync(ct);
        }

        await Send.NoContentAsync(ct);
    }
}