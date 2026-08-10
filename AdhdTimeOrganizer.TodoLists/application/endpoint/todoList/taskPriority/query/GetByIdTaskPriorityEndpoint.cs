using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.taskPriority.query;

public class GetByIdTaskPriorityEndpoint(
    DbContext dbContext)
    : BaseGetByIdEndpoint<TaskPriority, TaskPriorityResponse>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(TaskPriorityResponse entity, CancellationToken ct) => Task.FromResult(true);
}