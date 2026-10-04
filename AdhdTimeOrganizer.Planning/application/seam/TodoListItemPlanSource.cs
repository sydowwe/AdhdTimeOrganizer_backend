using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Planning.application.seam;

/// <summary>
/// Reports each to-do item's next open planner block through Core's seam, so TodoLists can say whether an item is
/// scheduled without referencing Planning. The join is <c>PlannerTask.TodolistItemId</c>, the navigation-free FK.
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
}
