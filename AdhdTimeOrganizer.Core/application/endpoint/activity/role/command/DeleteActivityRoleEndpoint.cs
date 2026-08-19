using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.role.command;

/// <summary>
/// Refuses to delete a role carrying a <see cref="ActivityRole.SystemKey"/>. Those three are what
/// quick-create files an activity under, and nothing in the UI can recreate one — there is no way to
/// attach a system key through the API — so a successful delete would leave quick-create permanently
/// 404ing in four dialogs. Renaming, recolouring and re-iconing a keyed role all stay allowed; only
/// its removal is blocked.
/// </summary>
public class DeleteActivityRoleEndpoint(DbContext dbContext)
    : BaseDeleteEndpoint<ActivityRole>(dbContext)
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        // Deliberately a separate read rather than the AuthorizeAsync hook: this is not an ownership
        // failure, and a bare 403 would tell the client nothing it can put in front of the user.
        var id = Route<long>("id");
        var systemKey = await dbContext.Set<ActivityRole>()
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => r.SystemKey)
            .FirstOrDefaultAsync(ct);

        if (systemKey is not null)
        {
            AddError("This is a system role the app files quick-created activities under, so it cannot be deleted. You can rename it instead.");
            await Send.ErrorsAsync(409, ct);
            return;
        }

        await base.HandleAsync(ct);
    }
}
