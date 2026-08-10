using AdhdTimeOrganizer.Core.domain.model.entity.activity.lookup;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.endpoint.@base.command;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.lookup.weatherDependency.command;

public class UpdateActivityWeatherDependencyEndpoint(DbContext dbContext)
    : BaseUpdateEndpoint<ActivityWeatherDependency, LookupRequest<ActivityWeatherDependency>>(dbContext);