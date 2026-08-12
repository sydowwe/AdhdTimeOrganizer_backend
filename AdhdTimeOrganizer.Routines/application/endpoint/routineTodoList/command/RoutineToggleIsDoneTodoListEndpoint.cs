using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;
using AdhdTimeOrganizer.Routines.domain.serviceContract;
using AdhdTimeOrganizer.TodoLists.application.endpoint.todoList;
using FastEndpoints;

namespace AdhdTimeOrganizer.Routines.application.endpoint.todoList.routineTodoList.command;

public class RoutineToggleIsDoneTodoListEndpoint(
    DbContext dbContext,
    IRoutinePeriodNotificationService notifier) : BaseToggleIsDoneTodoListEndpoint<RoutineTodoList>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;

    /// <summary>
    /// Periods this request reset lazily, collected in <see cref="FetchAndPrepare"/> and announced in
    /// <see cref="AfterSave"/> once the toggle is committed. Per-request state on a scoped endpoint instance.
    /// </summary>
    private readonly List<(RoutineTimePeriod Period, RoutineResetService.RoutinePeriodReset Result)> _reset = [];

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
            var result = RoutineResetService.TryReset(period, period.RoutineTodoListColl.ToList(), now);
            if (result is { } r)
            {
                _dbContext.Set<RoutinePeriodCompletion>().Add(r.Completion);
                _reset.Add((period, r));
            }
        }

        return Enumerable.Where(periods
                .SelectMany(p => p.RoutineTodoListColl), i => ids.Contains(i.Id))
            .ToList();
    }

    protected override void AfterItemToggled(RoutineTodoList entity, DateTime now)
    {
        RoutineResetService.UpdateItemStreak(entity, now);
    }

    /// <summary>
    /// Toggling an item can be what first notices an elapsed period, so this endpoint is a reset site and owes
    /// the same end-of-period summary the nightly job sends. Raised after the commit and best-effort — the
    /// user's toggle must not fail because a notification did.
    /// </summary>
    protected override async Task AfterSave(CancellationToken ct)
    {
        foreach (var (period, result) in _reset)
            await notifier.NotifyPeriodEndedAsync(period, result.Completion, result.Outcome, ct);
    }

    protected override async Task PublishEvent(RoutineTodoList entity, CancellationToken ct)
    {
        await new RoutineTodoListIsDoneChangedEvent(entity.ActivityId, entity.UserId, entity.IsDone)
            .PublishAsync(Mode.WaitForAll, ct);
    }
}