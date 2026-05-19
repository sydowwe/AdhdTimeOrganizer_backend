using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.weatherDependency.query;

public class GetAllActivityWeatherDependencyEndpoint(AppDbContext dbContext, LookupMapper<ActivityWeatherDependency> mapper)
    : BaseGetAllEndpoint<ActivityWeatherDependency, LookupResponse, LookupMapper<ActivityWeatherDependency>>(dbContext, mapper);
