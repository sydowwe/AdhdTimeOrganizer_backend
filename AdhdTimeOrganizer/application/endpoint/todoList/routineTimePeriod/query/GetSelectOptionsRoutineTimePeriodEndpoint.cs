using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTimePeriod.query;

public class GetSelectOptionsRoutineTimePeriodEndpoint(
    AppCommandDbContext appDbContext,
    RoutineTimePeriodMapper mapper)
    : BaseGetSelectOptionsEndpoint<RoutineTimePeriod, RoutineTimePeriodMapper>(appDbContext, mapper)
{
    protected override IQueryable<RoutineTimePeriod> Sort(IQueryable<RoutineTimePeriod> query) => query.OrderBy(tp => tp.LengthInDays);
}
