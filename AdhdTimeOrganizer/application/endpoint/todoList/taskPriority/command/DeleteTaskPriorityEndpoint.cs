using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.taskPriority.command;

public class DeleteTaskPriorityEndpoint(AppCommandDbContext dbContext)
    : BaseDeleteEndpoint<TaskPriority>(dbContext);
