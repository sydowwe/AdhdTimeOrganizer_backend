using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListCategory.query;

public class GetByIdTodoListCategoryEndpoint(DbContext dbContext)
    : BaseGetByIdEndpoint<TodoListCategory, TodoListCategoryResponse>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(TodoListCategoryResponse entity, CancellationToken ct) => Task.FromResult(true);
}