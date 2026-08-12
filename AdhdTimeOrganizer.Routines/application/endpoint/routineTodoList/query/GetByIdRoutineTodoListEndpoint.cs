using AdhdTimeOrganizer.Routines.application.dto.response.todoList;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Routines.application.endpoint.todoList.routineTodoList.query;

public class GetByIdRoutineTodoListEndpoint(
    DbContext dbContext)
    : BaseGetByIdEndpoint<RoutineTodoList, RoutineTodoListResponse>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(RoutineTodoListResponse entity, CancellationToken ct) => Task.FromResult(true);
}