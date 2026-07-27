using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.weatherDependency.command;

public class UpdateActivityWeatherDependencyEndpoint(AppDbContext dbContext)
    : BaseUpdateEndpoint<ActivityWeatherDependency, LookupRequest<ActivityWeatherDependency>>(dbContext);