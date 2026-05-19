using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.endpoint.@base.read.pageFilterSort;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.locationType.query;

public class GridActivityLocationTypeEndpoint(AppDbContext dbContext, LookupMapper<ActivityLocationType> mapper)
    : BaseGridEndpoint<ActivityLocationType, LookupResponse, LookupFilterRequest, LookupMapper<ActivityLocationType>>(dbContext, mapper)
{
    protected override IQueryable<ActivityLocationType> ApplyCustomFiltering(IQueryable<ActivityLocationType> query, LookupFilterRequest filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Text))
            query = query.Where(x => x.Text.Contains(filter.Text));
        return query;
    }
}
