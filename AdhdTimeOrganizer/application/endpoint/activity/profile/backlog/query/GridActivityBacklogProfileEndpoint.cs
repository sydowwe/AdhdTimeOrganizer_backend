using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.response.activity.profile;
using AdhdTimeOrganizer.application.endpoint.@base.read.pageFilterSort;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.domain.model.entity.activity.profile;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.profile.backlog.query;

public class GridActivityBacklogProfileEndpoint(AppDbContext dbContext)
    : BaseGridEndpoint<ActivityBacklogProfile, ActivityBacklogProfileResponse, ActivityBacklogProfileFilterRequest>(dbContext)
{
    public override string EndpointPath => "grid";

    protected override IQueryable<ActivityBacklogProfile> ApplyCustomFiltering(IQueryable<ActivityBacklogProfile> query,
        ActivityBacklogProfileFilterRequest filter)
    {
        var userId = User.GetId();

        query = query.Where(p => p.Activity.UserId == userId);

        if (filter.EnergyLevel.HasValue)
            query = query.Where(p => p.EnergyLevel == filter.EnergyLevel.Value);

        if (filter.EffortType.HasValue)
            query = query.Where(p => p.EffortType == filter.EffortType.Value);

        if (filter.IsRepeatable.HasValue)
            query = query.Where(p => p.IsRepeatable == filter.IsRepeatable.Value);

        return query;
    }
}
