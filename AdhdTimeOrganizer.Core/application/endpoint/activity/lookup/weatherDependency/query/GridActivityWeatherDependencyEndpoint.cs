using AdhdTimeOrganizer.Core.domain.model.entity.activity.lookup;
using Sydowwe.Framework.application.dto.request.filter;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.lookup.weatherDependency.query;

public class GridActivityWeatherDependencyEndpoint(DbContext dbContext)
    : BaseGridEndpoint<ActivityWeatherDependency, LookupResponse<ActivityWeatherDependency>, LookupFilter>(dbContext)
{
    protected override IQueryable<ActivityWeatherDependency> ApplyCustomFiltering(IQueryable<ActivityWeatherDependency> query, LookupFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Text))
            query = query.Where(x => x.Text.Contains(filter.Text));
        return query;
    }
}