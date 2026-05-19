using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.endpoint.@base.read.pageFilterSort;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.expectedCostTier.query;

public class GridActivityExpectedCostTierEndpoint(AppDbContext dbContext, LookupMapper<ActivityExpectedCostTier> mapper)
    : BaseGridEndpoint<ActivityExpectedCostTier, LookupResponse, LookupFilterRequest, LookupMapper<ActivityExpectedCostTier>>(dbContext, mapper)
{
    protected override IQueryable<ActivityExpectedCostTier> ApplyCustomFiltering(IQueryable<ActivityExpectedCostTier> query, LookupFilterRequest filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Text))
            query = query.Where(x => x.Text.Contains(filter.Text));
        return query;
    }
}
