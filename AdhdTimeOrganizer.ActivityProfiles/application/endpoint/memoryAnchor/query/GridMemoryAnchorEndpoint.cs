using AdhdTimeOrganizer.ActivityProfiles.application.dto.filter;
using AdhdTimeOrganizer.ActivityProfiles.application.dto.response;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.memoryAnchor.query;

public class GridMemoryAnchorEndpoint(DbContext dbContext)
    : BaseGridEndpoint<MemoryAnchor, MemoryAnchorResponse, MemoryAnchorFilterRequest>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;

    /// <summary>
    /// The caller's profiles, behind the Source column. Scoped by hand through the Activity because neither
    /// profile is <c>IEntityWithUser</c> — the host's <c>AppQueryFilters.ActivityProfileUserScope</c> is
    /// what backs that up, and this slice cannot see it.
    /// </summary>
    private IQueryable<ActivityBucketListProfile> ScopedBucketLists() =>
        _dbContext.Set<ActivityBucketListProfile>().Where(p => p.Activity.UserId == User.GetId());

    /// <inheritdoc cref="ScopedBucketLists"/>
    private IQueryable<ActivityBacklogProfile> ScopedBacklogs() =>
        _dbContext.Set<ActivityBacklogProfile>().Where(p => p.Activity.UserId == User.GetId());

    /// <summary>
    /// Overridden so the three Source fields are computed in SQL rather than overlaid afterwards: the base
    /// sorts the projected queryable, so a field filled in by PostProcessItems would sort on false.
    /// </summary>
    protected override Func<IQueryable<MemoryAnchor>, IQueryable<MemoryAnchorResponse>> Projection =>
        query => MemoryAnchorResponse.ProjectionWithProfiles(query, ScopedBucketLists(), ScopedBacklogs());

    /// <summary>
    /// The grid shows one <c>periodKey</c> column and the response stores the period as two members, so the
    /// key resolves nothing and <c>SortByMany</c> throws. Expanded into year-then-month rather than given a
    /// computed <c>year * 100 + month</c> proxy on the response: the two members are a prefix of
    /// <c>MemoryAnchorConfiguration</c>'s <c>(ActivityId, AnchorYear, AnchorMonth)</c> index, so a grid
    /// filtered to one activity — which the filter offers — orders straight off it, while the arithmetic
    /// form would need an expression index to do the same.
    /// </summary>
    /// <remarks>
    /// Both members inherit the column's single <c>isDesc</c>, so descending is
    /// <c>anchor_year DESC, anchor_month DESC</c> — a period is one thing to sort, not two that could
    /// disagree. Year leads because <c>SortByMany</c> appends each key with <c>ThenBy</c>.
    /// </remarks>
    protected override SortByRequest[] PreprocessSortBy(SortByRequest[] sortBy) =>
        sortBy.ExpandSortKey("periodKey", nameof(MemoryAnchorResponse.AnchorYear), nameof(MemoryAnchorResponse.AnchorMonth));

    protected override IQueryable<MemoryAnchor> ApplyCustomFiltering(IQueryable<MemoryAnchor> query, MemoryAnchorFilterRequest filter)
    {
        if (filter.AnchorMonth.HasValue)
            query = query.Where(m => m.AnchorMonth == filter.AnchorMonth.Value);

        if (filter.AnchorYear.HasValue)
            query = query.Where(m => m.AnchorYear == filter.AnchorYear.Value);

        if (filter.ActivityId.HasValue)
            query = query.Where(m => m.ActivityId == filter.ActivityId.Value);

        return query;
    }
}