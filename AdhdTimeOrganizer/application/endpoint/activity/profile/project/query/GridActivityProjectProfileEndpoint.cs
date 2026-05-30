using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.response.activity.profile;
using AdhdTimeOrganizer.application.endpoint.@base.read.pageFilterSort;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.domain.model.entity.activity.profile;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.profile.project.query;

public class GridActivityProjectProfileEndpoint(AppDbContext dbContext)
    : BaseGridEndpoint<ActivityProjectProfile, ActivityProjectProfileResponse, ActivityProjectProfileFilterRequest>(dbContext)
{
    public override string EndpointPath => "grid";

    protected override IQueryable<ActivityProjectProfile> ApplyCustomFiltering(IQueryable<ActivityProjectProfile> query,
        ActivityProjectProfileFilterRequest filter)
    {
        var userId = User.GetId();

        query = query.Where(p => p.Activity.UserId == userId);

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