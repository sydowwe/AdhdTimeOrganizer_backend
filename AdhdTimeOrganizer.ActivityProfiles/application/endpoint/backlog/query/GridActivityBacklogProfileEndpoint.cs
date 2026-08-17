using AdhdTimeOrganizer.ActivityProfiles.application.dto.filter;
using AdhdTimeOrganizer.ActivityProfiles.application.dto.response;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.backlog.query;

public class GridActivityBacklogProfileEndpoint(DbContext dbContext)
    : BaseGridEndpoint<ActivityBacklogProfile, ActivityBacklogProfileResponse, ActivityBacklogProfileFilterRequest>(dbContext)
{
    public override string EndpointPath => "grid";

    /// <summary>
    /// The caller's anchors. Scoped by hand for the same reason the profiles are: MemoryAnchor does carry a
    /// global query filter, but this subquery decides a field the client treats as "done", and it is one
    /// line to stop depending on a filter configured in another project.
    /// </summary>
    private IQueryable<MemoryAnchor> ScopedAnchors() => dbContext.Set<MemoryAnchor>().Where(m => m.UserId == User.GetId());

    /// <summary>
    /// Overridden so IsAnchored / MemoryAnchorId are computed in SQL rather than overlaid afterwards: the
    /// base sorts the projected queryable, so a field filled in by PostProcessItems would sort on false.
    /// </summary>
    protected override Func<IQueryable<ActivityBacklogProfile>, IQueryable<ActivityBacklogProfileResponse>> Projection =>
        query => ActivityBacklogProfileResponse.ProjectionWithAnchors(query, ScopedAnchors());

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

        if (filter.IsOneTime.HasValue)
            query = query.Where(p => p.IsRepeatable != filter.IsOneTime.Value);

        if (filter.IsAnchored.HasValue)
        {
            var anchors = ScopedAnchors();
            // Mirrors ProjectionWithAnchors: a repeatable entry is never anchored, so it belongs on the
            // false side whatever its activity's anchors say.
            query = filter.IsAnchored.Value
                ? query.Where(p => !p.IsRepeatable && anchors.Any(m => m.ActivityId == p.ActivityId))
                : query.Where(p => p.IsRepeatable || !anchors.Any(m => m.ActivityId == p.ActivityId));
        }

        return query;
    }
}