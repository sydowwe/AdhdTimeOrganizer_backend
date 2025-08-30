using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning;

public class GetAllRoutineToDoListsEndpoint(
    AppCommandDbContext dbContext,
    RoutineToDoListMapper mapper)
    : BaseGetAllEndpoint<RoutineToDoList, RoutineToDoListResponse, RoutineToDoListMapper>(dbContext, mapper)
{
    protected override IQueryable<RoutineToDoList> WithIncludes(IQueryable<RoutineToDoList> query)
    {
        return query
            .Include(rtdl => rtdl.Activity)
                .ThenInclude(a => a.Role)
            .Include(rtdl => rtdl.Activity)
                .ThenInclude(a => a.Category)
            .Include(rtdl => rtdl.TimePeriod);
    }
}
