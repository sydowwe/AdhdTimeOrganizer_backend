using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListItem.command;

public class DeleteTodoListItemEndpoint(AppDbContext dbContext)
    : BaseDeleteEndpoint<TodoListItem>(dbContext);