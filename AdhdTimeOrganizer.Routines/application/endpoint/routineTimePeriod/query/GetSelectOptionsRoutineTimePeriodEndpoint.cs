using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Routines.application.endpoint.todoList.routineTimePeriod.query;

public class GetSelectOptionsRoutineTimePeriodEndpoint(
    DbContext appDbContext)
    : BaseGetSelectOptionsEndpoint<RoutineTimePeriod>(appDbContext)
{
    protected override IQueryable<RoutineTimePeriod> Sort(IQueryable<RoutineTimePeriod> query)
    {
        return query.OrderBy(tp => tp.LengthInDays);
    }

    protected override IQueryable<SelectOptionResponse> Map(IQueryable<RoutineTimePeriod> query)
    {
        return query.Select(e => new SelectOptionResponse(e.Id, e.Text));
    }
}