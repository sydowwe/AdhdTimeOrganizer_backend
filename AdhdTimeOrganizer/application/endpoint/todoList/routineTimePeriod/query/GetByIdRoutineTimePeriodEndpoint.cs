using AdhdTimeOrganizer.application.dto.response.todoList;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTimePeriod.query;

public class GetByIdRoutineTimePeriodEndpoint(
    AppDbContext dbContext)
    : BaseGetByIdEndpoint<RoutineTimePeriod, RoutineTimePeriodResponse>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(RoutineTimePeriodResponse entity, CancellationToken ct) => Task.FromResult(true);
}