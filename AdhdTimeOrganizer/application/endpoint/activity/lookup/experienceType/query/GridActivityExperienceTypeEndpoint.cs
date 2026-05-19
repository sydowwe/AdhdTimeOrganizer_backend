using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.endpoint.@base.read.pageFilterSort;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.experienceType.query;

public class GridActivityExperienceTypeEndpoint(AppDbContext dbContext, LookupMapper<ActivityExperienceType> mapper)
    : BaseGridEndpoint<ActivityExperienceType, LookupResponse, LookupFilterRequest, LookupMapper<ActivityExperienceType>>(dbContext, mapper)
{
    protected override IQueryable<ActivityExperienceType> ApplyCustomFiltering(IQueryable<ActivityExperienceType> query, LookupFilterRequest filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Text))
            query = query.Where(x => x.Text.Contains(filter.Text));
        return query;
    }
}
