using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.routineTodoList.command;

public class UpdateRoutineTodoListEndpoint(AppCommandDbContext dbContext, RoutineTodoListMapper mapper)
    : BaseUpdateEndpoint<RoutineTodoList, UpdateRoutineTodoListRequest, RoutineTodoListResponse, RoutineTodoListMapper>(dbContext, mapper)
{
    protected override Task AfterMapping(RoutineTodoList entity, UpdateRoutineTodoListRequest req, CancellationToken ct = default)
    {
        if (req is { TotalCount: not null, DoneCount: null })
        {
            entity.DoneCount = 0;
        }
        return Task.CompletedTask;
    }
}
