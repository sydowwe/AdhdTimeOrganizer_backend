using AdhdTimeOrganizer.Reminders.application.dto.preference;
using AdhdTimeOrganizer.Reminders.domain.entity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Reminders.application.endpoint.preference;

/// <summary>
/// The caller's own reminder dispatch-policy preferences — per-kind opt-outs. Strictly self-scoped: it only
/// ever reads rows for <c>User.GetId()</c>, so there is no cross-user read path.
/// <para>
/// The quiet-hours window moved to the Notifications module (one window per user, deployment-wide) — see
/// <c>GET /notification-quiet-hours</c>.
/// </para>
/// </summary>
public class GetMyReminderPreferencesEndpoint(DbContext dbContext) : EndpointWithoutRequest<MyReminderPreferencesResponse>
{
    public override void Configure()
    {
        Get("/reminder-preference");
        Roles(IEndpoint.GetUserRole());
        Summary(s => s.Summary = "Get my reminder preferences (per-kind opt-outs)");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();

        var kindPreferences = await dbContext.Set<ReminderKindPreference>()
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.OwnerModule).ThenBy(p => p.Kind)
            .Select(p => new ReminderKindPreferenceDto
            {
                OwnerModule = p.OwnerModule,
                Kind = p.Kind,
                Enabled = p.Enabled,
                ChannelHint = p.ChannelHint
            })
            .ToListAsync(ct);

        await Send.OkAsync(new MyReminderPreferencesResponse { KindPreferences = kindPreferences }, ct);
    }
}