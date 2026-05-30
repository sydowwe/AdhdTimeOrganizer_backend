using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.response.activity.profile;
using AdhdTimeOrganizer.application.endpoint.@base.read.pageFilterSort;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.domain.model.entity.activity.profile;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.profile.bucketList.query;

public class GridActivityBucketListProfileEndpoint(AppDbContext dbContext)
    : BaseGridEndpoint<ActivityBucketListProfile, ActivityBucketListProfileResponse, ActivityBucketListProfileFilterRequest>(dbContext)
{
    public override string EndpointPath => "grid";

    protected override IQueryable<ActivityBucketListProfile> ApplyCustomFiltering(IQueryable<ActivityBucketListProfile> query,
        ActivityBucketListProfileFilterRequest filter)
    {
        var userId = User.GetId();

        query = query.Where(p => p.Activity.UserId == userId);

        if (filter.RequiresTravel.HasValue)
            query = query.Where(p => p.RequiresTravel == filter.RequiresTravel.Value);

        if (filter.ComfortZoneStep.HasValue)
            query = query.Where(p => p.ComfortZoneStep == filter.ComfortZoneStep.Value);

        return query;
    }
}
