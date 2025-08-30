using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskUrgency.command;

public class TaskUrgencyDeleteEndpoint(AppCommandDbContext dbContext)
    : BaseDeleteEndpoint<TaskUrgency>(dbContext);
