using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.todoList.command;

public class DeleteTodoListEndpoint(AppCommandDbContext dbContext)
    : BaseDeleteEndpoint<TodoList>(dbContext);
