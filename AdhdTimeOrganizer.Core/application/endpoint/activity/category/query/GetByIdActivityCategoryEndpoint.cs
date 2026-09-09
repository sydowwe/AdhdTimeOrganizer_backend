using AdhdTimeOrganizer.Core.application.dto.response.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.category.query;

public class GetByIdActivityCategoryEndpoint(
    DbContext dbContext)
    : BaseGetByIdEndpoint<ActivityCategory, ActivityCategoryResponse>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;

    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(ActivityCategoryResponse entity, CancellationToken ct) => Task.FromResult(true);

    /// <summary>
    /// Fills in <c>usageCount</c> / <c>canDelete</c>, which the static projection deliberately leaves at
    /// their defaults. Archived activities count: the delete blanks their <c>CategoryId</c> too.
    /// </summary>
    protected override async Task<ActivityCategoryResponse> PostProcessAsync(ActivityCategoryResponse entity, CancellationToken ct)
    {
        var usageCount = await _dbContext.Set<Activity>()
            .IgnoreQueryFilters([AppQueryFilters.ActivityArchived])
            .CountAsync(a => a.CategoryId == entity.Id, ct);

        return entity with { UsageCount = usageCount, CanDelete = usageCount == 0 };
    }
}
