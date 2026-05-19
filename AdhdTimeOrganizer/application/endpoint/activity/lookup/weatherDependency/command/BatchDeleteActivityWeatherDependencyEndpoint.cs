using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.lookup.weatherDependency.command;

public class BatchDeleteActivityWeatherDependencyEndpoint(AppDbContext dbContext)
    : BaseBatchDeleteEndpoint<ActivityWeatherDependency>(dbContext);
