using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.lookup.weatherDependency.query;

public class GetAllActivityWeatherDependencyEndpoint(DbContext dbContext)
    : BaseGetAllLookupEndpoint<ActivityWeatherDependency>(dbContext);