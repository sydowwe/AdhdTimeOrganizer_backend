using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.activity.command;

public class DeleteActivityEndpoint(AppCommandDbContext dbContext)
    : BaseDeleteEndpoint<Activity>(dbContext);
