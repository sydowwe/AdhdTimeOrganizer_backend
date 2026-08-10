using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.taskPriority.command;

public class DeleteTaskPriorityEndpoint(DbContext dbContext)
    : BaseDeleteEndpoint<TaskPriority>(dbContext);