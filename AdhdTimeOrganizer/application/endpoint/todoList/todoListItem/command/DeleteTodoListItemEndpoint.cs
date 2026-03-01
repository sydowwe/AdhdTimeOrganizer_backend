using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListItem.command;

public class DeleteTodoListItemEndpoint(AppDbContext dbContext)
    : BaseDeleteEndpoint<TodoListItem>(dbContext);
