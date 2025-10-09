using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.TaskPriority.command;

public class TaskPriorityDeleteEndpoint(AppCommandDbContext dbContext)
    : BaseDeleteEndpoint<TaskPriority>(dbContext);
