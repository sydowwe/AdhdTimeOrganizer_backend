using AdhdTimeOrganizer.Core.application.dto.filter;
using AdhdTimeOrganizer.Core.application.dto.response.activity.profile;
using AdhdTimeOrganizer.Core.domain.model.entity.activity.profile;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;
using Sydowwe.Framework.application.extensions;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.profile.backlog.query;

public class GridActivityBacklogProfileEndpoint(DbContext dbContext)
    : BaseGridEndpoint<ActivityBacklogProfile, ActivityBacklogProfileResponse, ActivityBacklogProfileFilterRequest>(dbContext)
{
    public override string EndpointPath => "grid";

    /// <summary>
    /// Ownership belongs here, not in <see cref="ApplyCustomFiltering"/> — the base only calls that one
    /// when the request carries a filter, so a caller sending <c>useFilter: false</c> would otherwise get
    /// every user's profiles. ActivityBacklogProfile is not IEntityWithUser, so no global query filter
    /// backs this up; the owner is reached through the Activity.
    /// </summary>
    protected override Task<IQueryable<ActivityBacklogProfile>> ApplyUserScoping(IQueryable<ActivityBacklogProfile> query, long userId,
        CancellationToken ct = default) =>
        Task.FromResult(query.Where(p => p.Activity.UserId == userId));

    protected override IQueryable<ActivityBacklogProfile> ApplyCustomFiltering(IQueryable<ActivityBacklogProfile> query,
        ActivityBacklogProfileFilterRequest filter)
    {
        if (filter.EnergyLevel.HasValue)
            query = query.Where(p => p.EnergyLevel == filter.EnergyLevel.Value);

        if (filter.EffortType.HasValue)
            query = query.Where(p => p.EffortType == filter.EffortType.Value);

        if (filter.IsRepeatable.HasValue)
            query = query.Where(p => p.IsRepeatable == filter.IsRepeatable.Value);

        return query;
    }
}