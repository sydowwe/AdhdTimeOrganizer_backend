using AdhdTimeOrganizer.ActivityProfiles.application.dto.filter;
using AdhdTimeOrganizer.ActivityProfiles.application.dto.response;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.project.query;

public class GridActivityProjectProfileEndpoint(DbContext dbContext)
    : BaseGridEndpoint<ActivityProjectProfile, ActivityProjectProfileResponse, ActivityProjectProfileFilterRequest>(dbContext)
{
    public override string EndpointPath => "grid";

    /// <summary>
    /// Ownership belongs here, not in <see cref="ApplyCustomFiltering"/> — the base only calls that one
    /// when the request carries a filter, so a caller sending <c>useFilter: false</c> would otherwise get
    /// every user's profiles. ActivityProjectProfile is not IEntityWithUser, so no global query filter
    /// backs this up; the owner is reached through the Activity.
    /// </summary>
    protected override Task<IQueryable<ActivityProjectProfile>> ApplyUserScoping(IQueryable<ActivityProjectProfile> query, long userId,
        CancellationToken ct = default) =>
        Task.FromResult(query.Where(p => p.Activity.UserId == userId));

    protected override IQueryable<ActivityProjectProfile> ApplyCustomFiltering(IQueryable<ActivityProjectProfile> query,
        ActivityProjectProfileFilterRequest filter)
    {
        if (filter.DifficultyLevel.HasValue)
            query = query.Where(p => p.DifficultyLevel == filter.DifficultyLevel.Value);

        if (filter.ReadinessStatus.HasValue)
            query = query.Where(p => p.ReadinessStatus == filter.ReadinessStatus.Value);

        if (filter.IsMessy.HasValue)
            query = query.Where(p => p.IsMessy == filter.IsMessy.Value);

        if (!string.IsNullOrWhiteSpace(filter.ProjectArea))
            query = query.Where(p => p.ProjectArea.Contains(filter.ProjectArea));

        return query;
    }
}