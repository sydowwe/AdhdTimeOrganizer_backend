using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoList.command;

public class DeleteTodoListEndpoint(AppDbContext dbContext)
    : BaseDeleteEndpoint<TodoList>(dbContext);
