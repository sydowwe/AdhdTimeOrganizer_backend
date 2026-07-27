using AdhdTimeOrganizer.application.dto.response.todoList;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTimePeriod.query;

public class GetAllRoutineTimePeriodEndpoint(
    AppDbContext dbContext)
    : BaseGetAllEndpoint<RoutineTimePeriod, RoutineTimePeriodResponse>(dbContext)
{
    protected override IQueryable<RoutineTimePeriod> Sort(IQueryable<RoutineTimePeriod> query)
    {
        return query.OrderBy(x => x.LengthInDays);
    }
}