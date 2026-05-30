using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.endpoint.@base.read.pageFilterSort;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.weatherDependency.query;

public class GridActivityWeatherDependencyEndpoint(AppDbContext dbContext)
    : BaseGridEndpoint<ActivityWeatherDependency, LookupResponse<ActivityWeatherDependency>, LookupFilterRequest>(dbContext)
{
    protected override IQueryable<ActivityWeatherDependency> ApplyCustomFiltering(IQueryable<ActivityWeatherDependency> query, LookupFilterRequest filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Text))
            query = query.Where(x => x.Text.Contains(filter.Text));
        return query;
    }
}
