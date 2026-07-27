using AdhdTimeOrganizer.application.dto.response.todoList;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTodoList.query;

public class GetByIdRoutineTodoListEndpoint(
    AppDbContext dbContext)
    : BaseGetByIdEndpoint<RoutineTodoList, RoutineTodoListResponse>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(RoutineTodoListResponse entity, CancellationToken ct) => Task.FromResult(true);
}