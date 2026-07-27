using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoList.command;

public class DeleteTodoListEndpoint(AppDbContext dbContext)
    : BaseDeleteEndpoint<TodoList>(dbContext);