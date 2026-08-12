using AdhdTimeOrganizer.ActivityProfiles.application.dto.filter;
using AdhdTimeOrganizer.ActivityProfiles.application.dto.response;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.bucketList.query;

public class GridActivityBucketListProfileEndpoint(DbContext dbContext)
    : BaseGridEndpoint<ActivityBucketListProfile, ActivityBucketListProfileResponse, ActivityBucketListProfileFilterRequest>(dbContext)
{
    public override string EndpointPath => "grid";

    /// <summary>
    /// Ownership belongs here, not in <see cref="ApplyCustomFiltering"/> — the base only calls that one
    /// when the request carries a filter, so a caller sending <c>useFilter: false</c> would otherwise get
    /// every user's profiles. ActivityBucketListProfile is not IEntityWithUser, so no global query filter
    /// backs this up; the owner is reached through the Activity.
    /// </summary>
    protected override Task<IQueryable<ActivityBucketListProfile>> ApplyUserScoping(IQueryable<ActivityBucketListProfile> query, long userId,
        CancellationToken ct = default) =>
        Task.FromResult(query.Where(p => p.Activity.UserId == userId));

    protected override IQueryable<ActivityBucketListProfile> ApplyCustomFiltering(IQueryable<ActivityBucketListProfile> query,
        ActivityBucketListProfileFilterRequest filter)
    {
        if (filter.RequiresTravel.HasValue)
            query = query.Where(p => p.RequiresTravel == filter.RequiresTravel.Value);

        if (filter.ComfortZoneStep.HasValue)
            query = query.Where(p => p.ComfortZoneStep == filter.ComfortZoneStep.Value);

        return query;
    }
}