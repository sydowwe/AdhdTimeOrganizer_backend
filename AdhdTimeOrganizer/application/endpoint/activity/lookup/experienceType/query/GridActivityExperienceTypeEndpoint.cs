using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.endpoint.@base.read.pageFilterSort;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.experienceType.query;

public class GridActivityExperienceTypeEndpoint(AppDbContext dbContext)
    : BaseGridEndpoint<ActivityExperienceType, LookupResponse<ActivityExperienceType>, LookupFilterRequest>(dbContext)
{
    protected override IQueryable<ActivityExperienceType> ApplyCustomFiltering(IQueryable<ActivityExperienceType> query, LookupFilterRequest filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Text))
            query = query.Where(x => x.Text.Contains(filter.Text));
        return query;
    }
}
