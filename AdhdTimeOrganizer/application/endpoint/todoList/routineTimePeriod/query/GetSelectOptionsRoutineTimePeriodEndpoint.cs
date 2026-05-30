using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTimePeriod.query;

public class GetSelectOptionsRoutineTimePeriodEndpoint(
    AppDbContext appDbContext)
    : BaseGetSelectOptionsEndpoint<RoutineTimePeriod>(appDbContext)
{
    protected override IQueryable<RoutineTimePeriod> Sort(IQueryable<RoutineTimePeriod> query) => query.OrderBy(tp => tp.LengthInDays);
    protected override IQueryable<SelectOptionResponse> Map(IQueryable<RoutineTimePeriod> query)
    {
        return query.Select(e => new SelectOptionResponse(e.Id, e.Text));
    }
}
