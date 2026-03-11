using AdhdTimeOrganizer.application.dto.request.toDoList;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.infrastructure.persistence.extensions;
using AdhdTimeOrganizer.infrastructure.settings;
using Microsoft.Extensions.Options;
using RoutineTodoListMapper = AdhdTimeOrganizer.application.mapper.todoList.RoutineTodoListMapper;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTodoList.command;

public class CreateRoutineToDoListEndpoint(AppDbContext dbContext, RoutineTodoListMapper mapper, IOptions<TodoListSettings> settings)
    : BaseCreateEndpoint<RoutineTodoList, UpdateRoutineTodoListRequest, RoutineTodoListMapper>(dbContext, mapper)
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly TodoListSettings _settings = settings.Value;

    protected override async Task AfterMapping(RoutineTodoList entity, UpdateRoutineTodoListRequest req, CancellationToken ct = default)
    {
        entity.DisplayOrder = await _dbContext.RoutineTodoLists.GetNextDisplayOrder(_settings, User.GetId(), entity.TimePeriodId, ct);

        if (req is { TotalCount: not null, DoneCount: null })
        {
            entity.DoneCount = 0;
        }
    }
}