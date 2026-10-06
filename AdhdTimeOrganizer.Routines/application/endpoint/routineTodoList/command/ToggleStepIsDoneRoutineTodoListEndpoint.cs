using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;
using AdhdTimeOrganizer.Routines.domain.serviceContract;
using AdhdTimeOrganizer.TodoLists.application.endpoint.todoList;
using FastEndpoints;

namespace AdhdTimeOrganizer.Routines.application.endpoint.todoList.routineTodoList.command;

public class ToggleStepIsDoneRoutineTodoListEndpoint(DbContext dbContext, IRoutineAwayDaysReader awayDaysReader)
    : BaseToggleStepIsDoneEndpoint<RoutineTodoList>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;

    /// <summary>
    /// The user's away days around the cycle <see cref="BeforeToggle"/> may close, read in <see cref="FetchItem"/>
    /// because that hook is synchronous. Per-request state on a scoped endpoint instance.
    /// </summary>
    private IReadOnlySet<DateOnly>? _awayDays;

    /// <summary>
    /// Ownership is filter-only here, deliberately: <c>RoutineTodoList</c> reaches <c>BaseEntityWithUser</c>,
    /// so AppDbContext's global <c>IEntityWithUser</c> filter scopes this lookup and another user's item id
    /// returns null, which the base answers as 404. Said out loud because the alternative reading is "forgot".
    /// </summary>
    protected override async Task<RoutineTodoList?> FetchItem(long itemId, CancellationToken ct)
    {
        var item = await _dbContext.Set<RoutineTodoList>()
            .Where(e => e.Id == itemId)
            .Include(e => e.RoutineTimePeriod)
            .Include(e => e.Steps)
            .FirstOrDefaultAsync(ct);

        if (item != null)
        {
            _awayDays = (await awayDaysReader.ForDueCyclesAsync(_dbContext, [item.RoutineTimePeriod], DateTime.UtcNow, ct))
                .GetValueOrDefault(item.UserId);
        }

        return item;
    }

    protected override bool BeforeToggle(RoutineTodoList item, DateTime now)
    {
        RoutineResetService.CheckGrace(item.RoutineTimePeriod, now);
        return RoutineResetService.TryReset(item.RoutineTimePeriod, item, now, _awayDays);
    }

    protected override async Task PublishEvent(RoutineTodoList item, CancellationToken ct)
    {
        await new RoutineTodoListIsDoneChangedEvent(item.ActivityId, item.UserId, item.IsDone)
            .PublishAsync(Mode.WaitForAll, ct);
    }

    protected override void OnItemDoneChanged(RoutineTodoList item, DateTime now)
    {
        RoutineResetService.UpdateItemStreak(item, now);
    }
}
