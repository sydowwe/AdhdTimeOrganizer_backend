using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Planning.application.seam;

/// <summary>
/// Reports each to-do item's next open planner block, and how its earlier blocks went, through Core's seam, so
/// TodoLists can say whether an item is scheduled — or keeps slipping — without referencing Planning. The join is
/// <c>PlannerTask.TodolistItemId</c>, the navigation-free FK.
/// </summary>
public sealed class TodoListItemPlanSource : ITodoListItemPlanSource, IScopedService
{
    public async Task<IReadOnlyDictionary<long, PlannedSlot>> NextPlannedSlotsAsync(
        DbContext db, long userId, IReadOnlyCollection<long> todoListItemIds, DateOnly fromDate, CancellationToken ct)
    {
        if (todoListItemIds.Count == 0)
            return new Dictionary<long, PlannedSlot>();

        var ids = todoListItemIds.Distinct().ToList();

        // UserId filtered by hand: this slice compiles against a plain DbContext and cannot rely on the host's
        // global filter, and the caller passes the id it authenticated.
        var rows = await db.Set<PlannerTask>()
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        && t.TodolistItemId != null
                        && ids.Contains(t.TodolistItemId.Value)
                        && t.Status != PlannerTaskStatus.Completed
                        && t.Status != PlannerTaskStatus.Cancelled
                        && t.Calendar.Date >= fromDate)
            .Select(t => new { ItemId = t.TodolistItemId!.Value, t.Calendar.Date, t.StartTime })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.ItemId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(r => r.Date).ThenBy(r => r.StartTime).Select(r => new PlannedSlot(r.Date, r.StartTime)).First());
    }

    public async Task<IReadOnlyDictionary<long, PlanHistory>> PlanHistoryAsync(
        DbContext db, long userId, IReadOnlyCollection<long> todoListItemIds, DateOnly today, CancellationToken ct)
    {
        if (todoListItemIds.Count == 0)
            return new Dictionary<long, PlanHistory>();

        var ids = todoListItemIds.Distinct().ToList();

        // Every linked block, whatever its day or status. Folded in memory: a handful of rows per item, and the
        // outcome sort below reads more clearly as a switch than as six conditional aggregates in SQL.
        var rows = await db.Set<PlannerTask>()
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        && t.TodolistItemId != null
                        && ids.Contains(t.TodolistItemId.Value))
            .Select(t => new
            {
                ItemId = t.TodolistItemId!.Value,
                t.Status,
                t.SkipReasonKey,
                t.CarryOverCount,
                t.PostponeCount,
                t.IsBackground,
                t.Calendar.Date
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.ItemId)
            .ToDictionary(g => g.Key, g =>
            {
                var cancelled = g.Where(r => r.Status == PlannerTaskStatus.Cancelled).ToList();
                return new PlanHistory(
                    Completed: g.Count(r => r.Status == PlannerTaskStatus.Completed),
                    CarriedOver: g.Sum(r => r.CarryOverCount),
                    // The FO4 "missed" rule with the whole day behind the user: never started, and not a
                    // background block, which runs alongside the day rather than being an attempt at it.
                    Missed: g.Count(r => (r.Status is PlannerTaskStatus.NotStarted or PlannerTaskStatus.OnHold)
                                         && !r.IsBackground
                                         && r.Date < today),
                    SkippedNoTime: cancelled.Count(r => r.SkipReasonKey == SkipReasonKey.NoTime),
                    SkippedNoEnergy: cancelled.Count(r => r.SkipReasonKey == SkipReasonKey.NoEnergy),
                    SkippedNotRelevant: cancelled.Count(r => r.SkipReasonKey == SkipReasonKey.NotRelevant),
                    SkippedOther: cancelled.Count(r => r.SkipReasonKey == null),
                    Postponed: g.Sum(r => r.PostponeCount));
            });
    }
}
