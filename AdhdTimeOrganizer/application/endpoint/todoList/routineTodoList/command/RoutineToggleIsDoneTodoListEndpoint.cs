using AdhdTimeOrganizer.application.dto.request.generic;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.domain.service;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.todoList.routineTodoList.command;

public class RoutineToggleIsDoneTodoListEndpoint(AppDbContext dbContext)
    : BaseToggleIsDoneTodoListEndpoint<RoutineTodoList>(dbContext)
{
    public override async Task HandleAsync(IdListRequest request, CancellationToken ct)
    {
        var timePeriodIds = await dbContext.Set<RoutineTodoList>()
            .Where(e => request.Ids.Contains(e.Id))
            .Select(e => e.TimePeriodId)
            .Distinct()
            .ToListAsync(ct);

        if (timePeriodIds.Count == 0)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var periods = await dbContext.Set<RoutineTimePeriod>()
            .Where(tp => timePeriodIds.Contains(tp.Id))
            .Include(tp => tp.RoutineTodoListColl)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;

        foreach (var period in periods)
        {
            var allItems = period.RoutineTodoListColl.ToList();
            RoutineResetService.CheckGrace(period, now);
            RoutineResetService.TryReset(period, allItems, now);
        }

        var itemsToToggle = periods
            .SelectMany(p => p.RoutineTodoListColl)
            .Where(i => request.Ids.Contains(i.Id))
            .ToList();

        foreach (var item in itemsToToggle)
        {
            IsDoneLogic(item);
            RoutineResetService.UpdateItemStreak(item, now);
        }

        await dbContext.SaveChangesAsync(ct);
        await SendNoContentAsync(ct);
    }
}
