using AdhdTimeOrganizer.Notifications.application.dto;
using AdhdTimeOrganizer.Notifications.domain.entity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Notifications.application.endpoint.preference;

/// <summary>Upserts the current user's opt-out for one (type, channel) pair.</summary>
public class UpdateNotificationPreferenceEndpoint(DbContext dbContext) : Endpoint<UpdateNotificationPreferenceRequest>
{
    public override void Configure()
    {
        Put("/notification-preference");
        Roles(IEndpoint.GetUserRole());
        Summary(s => s.Summary = "Enable or disable a notification channel for a notification type");
    }

    public override async Task HandleAsync(UpdateNotificationPreferenceRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        var pref = await dbContext.Set<NotificationPreference>()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Type == req.Type && x.Channel == req.Channel, ct);

        if (pref is null)
        {
            await dbContext.Set<NotificationPreference>().AddAsync(new NotificationPreference
            {
                UserId = userId,
                Type = req.Type,
                Channel = req.Channel,
                Enabled = req.Enabled
            }, ct);

            try
            {
                await dbContext.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Concurrent insert won the race — detach and fall through to update.
                dbContext.ChangeTracker.Clear();
                pref = await dbContext.Set<NotificationPreference>()
                    .FirstAsync(x => x.UserId == userId && x.Type == req.Type && x.Channel == req.Channel, ct);
                pref.Enabled = req.Enabled;
                await dbContext.SaveChangesAsync(ct);
            }
        }
        else
        {
            pref.Enabled = req.Enabled;
            await dbContext.SaveChangesAsync(ct);
        }

        await Send.NoContentAsync(ct);
    }
}