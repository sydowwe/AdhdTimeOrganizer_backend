using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;
using AdhdTimeOrganizer.Routines.domain.serviceContract;
using AdhdTimeOrganizer.Routines.infrastructure.persistence;
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

    /// <summary>
    /// Ownership is filter-only here, deliberately: <c>RoutineTodoList</c> and <c>RoutineTimePeriod</c> both
    /// reach <c>BaseEntityWithUser</c>, so AppDbContext's global <c>IEntityWithUser</c> filter scopes the id
    /// lookup, <see cref="LoadPeriodsAsync"/> and the toggle alike — a foreign id matches nothing, this
    /// returns empty and the base answers 404. Said out loud because the alternative reading is "forgot".
    /// </summary>
    protected override async Task<List<RoutineTodoList>> FetchAndPrepare(ICollection<long> ids, DateTime now, CancellationToken ct)
    {
        var timePeriodIds = await _dbContext.Set<RoutineTodoList>()
            .Where(e => ids.Contains(e.Id))
            .Select(e => e.TimePeriodId)
            .Distinct()
            .ToListAsync(ct);

        if (timePeriodIds.Count == 0)
            return [];

        var periods = await LoadPeriodsAsync(timePeriodIds, ct);

        var changed = false;
        foreach (var period in periods)
        {
            changed |= RoutineResetService.CheckGrace(period, now);
            // Catch-up, not a single cycle: a toggle can be the first thing to notice a period that rolled over
            // several times, and it must leave that period current.
            var result = RoutineResetService.TryResetCatchUp(period, period.RoutineTodoListColl.ToList(), now);
            if (result is { } r)
            {
                _dbContext.Set<RoutinePeriodCompletion>().AddRange(r.Completions);
                _reset.Add((period, r.Latest));
                changed = true;
            }
        }

        if (changed)
        {
            try
            {
                // Committed on its own, ahead of the toggle, because it is the only part of this request that
                // can lose a race: three call sites apply the same lazy reset and none of them locks. Splitting
                // it off keeps the recovery below from having to reconstruct the toggle as well.
                await _dbContext.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (RoutineResetConflict.IsLostRace(ex))
            {
                // Another caller closed the cycle first, writing what we were about to (see
                // RoutineResetConflict). Discard our copy and re-read, so the toggle below lands on the
                // committed state rather than on a change set the database has already rejected.
                _dbContext.ChangeTracker.Clear();
                // The winner announces the reset; we only toggle.
                _reset.Clear();
                periods = await LoadPeriodsAsync(timePeriodIds, ct);
            }
        }

        return Enumerable.Where(periods
                .SelectMany(p => p.RoutineTodoListColl), i => ids.Contains(i.Id))
            .ToList();
    }

    private Task<List<RoutineTimePeriod>> LoadPeriodsAsync(List<long> timePeriodIds, CancellationToken ct) =>
        _dbContext.Set<RoutineTimePeriod>()
            .Where(tp => timePeriodIds.Contains(tp.Id))
            .Include(tp => tp.RoutineTodoListColl)
            .ThenInclude(i => i.Steps)
            .ToListAsync(ct);

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