using AdhdTimeOrganizer.application.dto.response.toDoList;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTodoList.query;

public class GetAllRoutineTodoListEndpoint(
    AppCommandDbContext dbContext,
    RoutineTodoListMapper mapper)
    : BaseGetAllEndpoint<RoutineTodoList, RoutineTodoListResponse, RoutineTodoListMapper>(dbContext, mapper)
{
    protected override IQueryable<RoutineTodoList> WithIncludes(IQueryable<RoutineTodoList> query)
    {
        return query
            .Include(rtdl => rtdl.Activity)
                .ThenInclude(a => a.Role)
            .Include(rtdl => rtdl.Activity)
                .ThenInclude(a => a.Category)
            .Include(rtdl => rtdl.RoutineTimePeriod);
    }
}
