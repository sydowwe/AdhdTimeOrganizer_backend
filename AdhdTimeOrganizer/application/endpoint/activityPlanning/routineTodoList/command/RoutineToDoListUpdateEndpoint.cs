using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.routineTodoList.command;

public class RoutineTodoListUpdateEndpoint(AppCommandDbContext dbContext, RoutineTodoListMapper mapper)
    : BaseUpdateEndpoint<RoutineTodoList, RoutineTodoListRequest, RoutineTodoListResponse, RoutineTodoListMapper>(dbContext, mapper)
{
    protected override void AfterMapping(RoutineTodoList entity, RoutineTodoListRequest req)
    {
        if (req is { TotalCount: not null, DoneCount: null })
        {
            entity.DoneCount = 0;
        }
    }
}
