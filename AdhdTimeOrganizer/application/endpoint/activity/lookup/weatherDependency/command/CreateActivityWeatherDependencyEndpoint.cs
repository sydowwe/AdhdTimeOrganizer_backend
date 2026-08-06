using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.weatherDependency.command;

public class CreateActivityWeatherDependencyEndpoint(AppDbContext dbContext)
    : BaseCreateEndpoint<ActivityWeatherDependency, LookupRequest<ActivityWeatherDependency>>(dbContext);