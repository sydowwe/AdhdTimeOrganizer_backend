using AdhdTimeOrganizer.Reminders.application.dto.preference;
using AdhdTimeOrganizer.Reminders.domain.entity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Reminders.application.endpoint.preference;

/// <summary>
/// Upserts the caller's per-kind opt-out for one <c>(OwnerModule, Kind)</c>. Self-scoped by <c>User.GetId()</c>.
/// Mirrors <c>UpdateNotificationPreferenceEndpoint</c>, including the concurrent-insert race fallback.
/// </summary>
public class UpdateReminderKindPreferenceEndpoint(DbContext dbContext) : Endpoint<UpdateReminderKindPreferenceRequest>
{
    public override void Configure()
    {
        Put("/reminder-preference/kind");
        Roles(IEndpoint.GetUserRole());
        Summary(s => s.Summary = "Enable or mute reminders of a given kind for me");
    }

    public override async Task HandleAsync(UpdateReminderKindPreferenceRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.OwnerModule) || string.IsNullOrWhiteSpace(req.Kind))
        {
            AddError("OwnerModule and Kind are required.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        var userId = User.GetId();
        var pref = await dbContext.Set<ReminderKindPreference>()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.OwnerModule == req.OwnerModule && x.Kind == req.Kind, ct);

        if (pref is null)
        {
            dbContext.Add(new ReminderKindPreference
            {
                UserId = userId,
                OwnerModule = req.OwnerModule,
                Kind = req.Kind,
                Enabled = req.Enabled,
                ChannelHint = req.ChannelHint
            });

            try
            {
                await dbContext.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Concurrent insert won the race — detach and fall through to update.
                dbContext.ChangeTracker.Clear();
                pref = await dbContext.Set<ReminderKindPreference>()
                    .FirstAsync(x => x.UserId == userId && x.OwnerModule == req.OwnerModule && x.Kind == req.Kind, ct);
                pref.Enabled = req.Enabled;
                pref.ChannelHint = req.ChannelHint;
                await dbContext.SaveChangesAsync(ct);
            }
        }
        else
        {
            pref.Enabled = req.Enabled;
            pref.ChannelHint = req.ChannelHint;
            await dbContext.SaveChangesAsync(ct);
        }

        await Send.NoContentAsync(ct);
    }
}