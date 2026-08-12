using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListItem.query;

public class GetByIdTodoListItemEndpoint(DbContext dbContext)
    : BaseGetByIdEndpoint<TodoListItem, TodoListItemResponse>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(TodoListItemResponse entity, CancellationToken ct) => Task.FromResult(true);
}