using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.todoList.taskPriority.query;

public class GetByIdTaskPriorityEndpoint(
    AppDbContext dbContext)
    : BaseGetByIdEndpoint<TaskPriority, TaskPriorityResponse>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(TaskPriorityResponse entity, CancellationToken ct) => Task.FromResult(true);
}