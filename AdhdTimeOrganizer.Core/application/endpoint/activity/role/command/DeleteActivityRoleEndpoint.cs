using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.role.command;

/// <summary>
/// Two refusals, both 409, both about a delete that would destroy more than the row it names.
///
/// <para><b>System roles.</b> A role carrying a <see cref="ActivityRole.SystemKey"/> is what
/// quick-create files an activity under, and nothing in the UI can recreate one — there is no way to
/// attach a system key through the API — so a successful delete would leave quick-create permanently
/// 404ing in four dialogs. Renaming, recolouring and re-iconing a keyed role all stay allowed; only
/// its removal is blocked.</para>
///
/// <para><b>Roles that still have activities.</b> <c>Activity.RoleId</c> is <c>NOT NULL</c>, so
/// <c>SetNull</c> is not available and the FK cascades: deleting the role would delete every activity
/// under it, and each of those cascades again through every activity FK in the solution — history rows,
/// planner tasks, to-do items, routine to-do lists, tracker mappings, all three profiles, memory
/// anchors, timer presets. Refusing is the only non-destructive answer; the caller reassigns or deletes
/// the activities first. Reuses the 409 the system-key path already established so the client needs no
/// new branch, and mirrors the <c>canDelete</c> the role reads now carry.</para>
/// </summary>
public class DeleteActivityRoleEndpoint(DbContext dbContext)
    : BaseDeleteEndpoint<ActivityRole>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;

    public override async Task HandleAsync(CancellationToken ct)
    {
        // Deliberately a separate read rather than the AuthorizeAsync hook: this is not an ownership
        // failure, and a bare 403 would tell the client nothing it can put in front of the user.
        var id = Route<long>("id");
        var systemKey = await _dbContext.Set<ActivityRole>()
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

        // The archival filter is lifted deliberately: an archived activity cascades exactly like an active
        // one, and it is the one a user is least likely to remember owning. Counting only active rows would
        // hand back 204 and destroy the history the archive was keeping. The per-user filter stays on, so a
        // foreign role counts 0 here and falls through to the base's 404 rather than leaking a count.
        var activityCount = await _dbContext.Set<Activity>()
            .IgnoreQueryFilters([AppQueryFilters.ActivityArchived])
            .CountAsync(a => a.RoleId == id, ct);

        if (activityCount > 0)
        {
            AddError($"{activityCount} activities are still filed under this role, and deleting it would delete them and their history too. Move them to another role first.");
            await Send.ErrorsAsync(409, ct);
            return;
        }

        await base.HandleAsync(ct);
    }
}
