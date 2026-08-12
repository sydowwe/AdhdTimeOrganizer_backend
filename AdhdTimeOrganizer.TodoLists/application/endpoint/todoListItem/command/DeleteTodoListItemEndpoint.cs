using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListItem.command;

public class DeleteTodoListItemEndpoint(DbContext dbContext)
    : BaseDeleteEndpoint<TodoListItem>(dbContext);