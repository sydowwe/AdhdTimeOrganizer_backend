using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.weatherDependency.query;

public class GetByIdActivityWeatherDependencyEndpoint(AppDbContext dbContext)
    : BaseGetByIdEndpoint<ActivityWeatherDependency, LookupResponse<ActivityWeatherDependency>>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(LookupResponse<ActivityWeatherDependency> entity, CancellationToken ct) => Task.FromResult(true);
}