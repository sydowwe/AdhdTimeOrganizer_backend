using AdhdTimeOrganizer.application.dto.response.todoList;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoList.query;

public class GetByIdTodoListEndpoint(AppDbContext dbContext)
    : BaseGetByIdEndpoint<TodoList, TodoListResponse>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(TodoListResponse entity, CancellationToken ct) => Task.FromResult(true);
}