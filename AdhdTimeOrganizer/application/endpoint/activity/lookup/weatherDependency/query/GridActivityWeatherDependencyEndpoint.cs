using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.dto.request.filter;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.weatherDependency.query;

public class GridActivityWeatherDependencyEndpoint(AppDbContext dbContext)
    : BaseGridEndpoint<ActivityWeatherDependency, LookupResponse<ActivityWeatherDependency>, LookupFilter>(dbContext)
{
    protected override IQueryable<ActivityWeatherDependency> ApplyCustomFiltering(IQueryable<ActivityWeatherDependency> query, LookupFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Text))
            query = query.Where(x => x.Text.Contains(filter.Text));
        return query;
    }
}