using AdhdTimeOrganizer.application.endpoint.todoList;
using AdhdTimeOrganizer.application.@event;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.domain.service;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTodoList.command;

public class RoutineToggleIsDoneTodoListEndpoint(AppDbContext dbContext) : BaseToggleIsDoneTodoListEndpoint<RoutineTodoList>(dbContext)
{
    private readonly AppDbContext _dbContext = dbContext;

    protected override async Task<List<RoutineTodoList>> FetchAndPrepare(ICollection<long> ids, DateTime now, CancellationToken ct)
    {
        var timePeriodIds = await _dbContext.Set<RoutineTodoList>()
            .Where(e => ids.Contains(e.Id))
            .Select(e => e.TimePeriodId)
            .Distinct()
            .ToListAsync(ct);

        if (timePeriodIds.Count == 0)
            return [];

        var periods = await _dbContext.Set<RoutineTimePeriod>()
            .Where(tp => timePeriodIds.Contains(tp.Id))
            .Include(tp => tp.RoutineTodoListColl)
            .ThenInclude(i => i.Steps)
            .ToListAsync(ct);

        foreach (var period in periods)
        {
            RoutineResetService.CheckGrace(period, now);
            RoutineResetService.TryReset(period, period.RoutineTodoListColl.ToList(), now);
        }

        return periods
            .SelectMany(p => p.RoutineTodoListColl)
            .Where(i => ids.Contains(i.Id))
            .ToList();
    }

    protected override void AfterItemToggled(RoutineTodoList entity, DateTime now) =>
        RoutineResetService.UpdateItemStreak(entity, now);

    protected override async Task PublishEvent(RoutineTodoList entity, CancellationToken ct) =>
        await new RoutineTodoListIsDoneChangedEvent(entity.ActivityId, entity.UserId, entity.IsDone)
            .PublishAsync(Mode.WaitForAll, ct);
}
