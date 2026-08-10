using AdhdTimeOrganizer.Routines.application.dto.response.todoList;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Routines.application.endpoint.todoList.routineTimePeriod.query;

public class GetAllRoutineTimePeriodEndpoint(
    DbContext dbContext)
    : BaseGetAllEndpoint<RoutineTimePeriod, RoutineTimePeriodResponse>(dbContext)
{
    protected override IQueryable<RoutineTimePeriod> Sort(IQueryable<RoutineTimePeriod> query)
    {
        return query.OrderBy(x => x.LengthInDays);
    }
}