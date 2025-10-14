using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskPriority.command;

public class DeleteTaskPriorityEndpoint(AppCommandDbContext dbContext)
    : BaseDeleteEndpoint<TaskPriority>(dbContext);
