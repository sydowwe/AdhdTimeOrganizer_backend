using AdhdTimeOrganizer.application.endpoint.todoList;
using AdhdTimeOrganizer.application.@event;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.domain.service;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTodoList.command;

public class ToggleStepIsDoneRoutineTodoListEndpoint(AppDbContext dbContext)
    : BaseToggleStepIsDoneEndpoint<RoutineTodoList>(dbContext)
{
    private readonly AppDbContext _dbContext = dbContext;

    protected override async Task<RoutineTodoList?> FetchItem(long itemId, CancellationToken ct) =>
        await _dbContext.Set<RoutineTodoList>()
            .Where(e => e.Id == itemId)
            .Include(e => e.RoutineTimePeriod)
            .Include(e => e.Steps)
            .FirstOrDefaultAsync(ct);

    protected override bool BeforeToggle(RoutineTodoList item, DateTime now)
    {
        RoutineResetService.CheckGrace(item.RoutineTimePeriod, now);
        return RoutineResetService.TryReset(item.RoutineTimePeriod, item, now);
    }

    protected override async Task PublishEvent(RoutineTodoList item, CancellationToken ct) =>
        await new RoutineTodoListIsDoneChangedEvent(item.ActivityId, item.UserId, item.IsDone)
            .PublishAsync(Mode.WaitForAll, ct);

    protected override void OnItemCompleted(RoutineTodoList item, DateTime now) =>
        RoutineResetService.UpdateItemStreak(item, now);
}
