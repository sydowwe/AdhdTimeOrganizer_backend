using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;
using AdhdTimeOrganizer.TodoLists.application.endpoint.todoList;
using FastEndpoints;

namespace AdhdTimeOrganizer.Routines.application.endpoint.todoList.routineTodoList.command;

public class ToggleStepIsDoneRoutineTodoListEndpoint(DbContext dbContext)
    : BaseToggleStepIsDoneEndpoint<RoutineTodoList>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;

    protected override async Task<RoutineTodoList?> FetchItem(long itemId, CancellationToken ct)
    {
        return await _dbContext.Set<RoutineTodoList>()
            .Where(e => e.Id == itemId)
            .Include(e => e.RoutineTimePeriod)
            .Include(e => e.Steps)
            .FirstOrDefaultAsync(ct);
    }

    protected override bool BeforeToggle(RoutineTodoList item, DateTime now)
    {
        RoutineResetService.CheckGrace(item.RoutineTimePeriod, now);
        return RoutineResetService.TryReset(item.RoutineTimePeriod, item, now);
    }

    protected override async Task PublishEvent(RoutineTodoList item, CancellationToken ct)
    {
        await new RoutineTodoListIsDoneChangedEvent(item.ActivityId, item.UserId, item.IsDone)
            .PublishAsync(Mode.WaitForAll, ct);
    }

    protected override void OnItemCompleted(RoutineTodoList item, DateTime now)
    {
        RoutineResetService.UpdateItemStreak(item, now);
    }
}