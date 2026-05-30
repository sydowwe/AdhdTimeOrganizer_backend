using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.endpoint.@base.read.pageFilterSort;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.expectedCostTier.query;

public class GridActivityExpectedCostTierEndpoint(AppDbContext dbContext)
    : BaseGridEndpoint<ActivityExpectedCostTier, LookupResponse<ActivityExpectedCostTier>, LookupFilterRequest>(dbContext)
{
    protected override IQueryable<ActivityExpectedCostTier> ApplyCustomFiltering(IQueryable<ActivityExpectedCostTier> query, LookupFilterRequest filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Text))
            query = query.Where(x => x.Text.Contains(filter.Text));
        return query;
    }
}
