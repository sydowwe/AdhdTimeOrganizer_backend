using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.todoList.taskPriority.command;

public class DeleteTaskPriorityEndpoint(AppDbContext dbContext)
    : BaseDeleteEndpoint<TaskPriority>(dbContext);