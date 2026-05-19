using AdhdTimeOrganizer.application.dto.request.generic;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.weatherDependency.command;

public class CreateActivityWeatherDependencyEndpoint(AppDbContext dbContext, LookupMapper<ActivityWeatherDependency> mapper)
    : BaseCreateEndpoint<ActivityWeatherDependency, SelectOptionRequest, LookupMapper<ActivityWeatherDependency>>(dbContext, mapper);
