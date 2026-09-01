using AdhdTimeOrganizer.Core.application.dto.response.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.role.query;

public class GetByIdActivityRoleEndpoint(
    DbContext dbContext)
    : BaseGetByIdEndpoint<ActivityRole, ActivityRoleResponse>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;

    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(ActivityRoleResponse entity, CancellationToken ct) => Task.FromResult(true);

    /// <summary>
    /// Fills in <c>usageCount</c> / <c>canDelete</c>, which the static projection deliberately leaves at
    /// their defaults. A second round trip rather than a correlated subquery, because there is nothing to
    /// sort on one row — same shape as <c>GetByIdActivityEndpoint</c>.
    /// </summary>
    protected override async Task<ActivityRoleResponse> PostProcessAsync(ActivityRoleResponse entity, CancellationToken ct)
    {
        var usageCount = await _dbContext.Set<Activity>()
            .IgnoreQueryFilters([AppQueryFilters.ActivityArchived])
            .CountAsync(a => a.RoleId == entity.Id, ct);

        // Both halves of DeleteActivityRoleEndpoint's refusal, so canDelete really does predict its answer.
        return entity with { UsageCount = usageCount, CanDelete = usageCount == 0 && entity.SystemKey is null };
    }
}
