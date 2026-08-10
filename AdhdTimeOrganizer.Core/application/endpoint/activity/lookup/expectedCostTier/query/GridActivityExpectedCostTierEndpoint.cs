using AdhdTimeOrganizer.Core.domain.model.entity.activity.lookup;
using Sydowwe.Framework.application.dto.request.filter;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.lookup.expectedCostTier.query;

public class GridActivityExpectedCostTierEndpoint(DbContext dbContext)
    : BaseGridEndpoint<ActivityExpectedCostTier, LookupResponse<ActivityExpectedCostTier>, LookupFilter>(dbContext)
{
    protected override IQueryable<ActivityExpectedCostTier> ApplyCustomFiltering(IQueryable<ActivityExpectedCostTier> query, LookupFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Text))
            query = query.Where(x => x.Text.Contains(filter.Text));
        return query;
    }
}