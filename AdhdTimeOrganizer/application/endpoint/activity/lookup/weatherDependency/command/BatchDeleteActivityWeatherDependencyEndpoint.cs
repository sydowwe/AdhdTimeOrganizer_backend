using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.weatherDependency.command;

public class BatchDeleteActivityWeatherDependencyEndpoint(AppDbContext dbContext)
    : BaseBatchDeleteEndpoint<ActivityWeatherDependency>(dbContext);