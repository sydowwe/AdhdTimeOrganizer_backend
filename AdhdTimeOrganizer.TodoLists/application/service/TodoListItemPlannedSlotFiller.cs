using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.TodoLists.application.service;

/// <summary>
/// Overlays the planner's view of each item onto already-projected items, via <see cref="ITodoListItemPlanSource"/>:
/// <see cref="TodoListItemResponse.NextPlannedSlot"/> (when it is next scheduled) and
/// <see cref="TodoListItemResponse.PlanHelp"/> (whether it keeps slipping, FO3). One query per field however many
/// items, and "today" is the user's — a block planned for today stays "scheduled" all day on their clock, not
/// until UTC's midnight.
/// <para>
/// Static rather than a service: the lifetime-marker scan registers by implemented interface only, and an
/// interface with one implementation and three callers would be ceremony. The endpoints inject the two
/// collaborators themselves.
/// </para>
/// </summary>
public static class TodoListItemPlannedSlotFiller
{
    /// <summary>Both fields — the list read and the single-item read.</summary>
    public static async Task<List<TodoListItemResponse>> FillAsync(
        List<TodoListItemResponse> items,
        DbContext dbContext,
        ITodoListItemPlanSource planSource,
        IUserTimeZoneResolver timeZones,
        long userId,
        CancellationToken ct)
    {
        if (items.Count == 0)
            return items;

        var today = await UserTodayAsync(timeZones, userId, ct);
        var ids = items.Select(i => i.Id).ToList();
        // Sequential, not WhenAll: both run on the one request-scoped DbContext.
        var slots = await planSource.NextPlannedSlotsAsync(dbContext, userId, ids, today, ct);
        var histories = await planSource.PlanHistoryAsync(dbContext, userId, ids, today, ct);

        return items
            .Select(i => i with
            {
                NextPlannedSlot = slots.TryGetValue(i.Id, out var slot) ? PlannedSlotResponse.From(slot) : null,
                PlanHelp = PlanHelpFor(i, histories)
            })
            .ToList();
    }

    /// <summary>
    /// <see cref="TodoListItemResponse.PlanHelp"/> alone — the dashboard widget. Its rows are chosen by due date,
    /// and it never carried the planned slot, so adding a planned chip to home is left as a separate decision.
    /// </summary>
    public static async Task<List<TodoListItemResponse>> FillPlanHelpAsync(
        List<TodoListItemResponse> items,
        DbContext dbContext,
        ITodoListItemPlanSource planSource,
        IUserTimeZoneResolver timeZones,
        long userId,
        CancellationToken ct)
    {
        if (items.Count == 0)
            return items;

        var today = await UserTodayAsync(timeZones, userId, ct);
        var histories = await planSource.PlanHistoryAsync(dbContext, userId, items.Select(i => i.Id).ToList(), today, ct);

        return items.Select(i => i with { PlanHelp = PlanHelpFor(i, histories) }).ToList();
    }

    /// <summary>A done item needs no help starting, whatever its blocks did.</summary>
    private static string? PlanHelpFor(TodoListItemResponse item, IReadOnlyDictionary<long, PlanHistory> histories) =>
        item.IsDone ? null : TodoListItemPlanHelp.For(histories.GetValueOrDefault(item.Id));

    private static async Task<DateOnly> UserTodayAsync(IUserTimeZoneResolver timeZones, long userId, CancellationToken ct) =>
        DateOnly.FromDateTime(WallClockZone.FromUtc(DateTime.UtcNow, await timeZones.GetAsync(userId, ct)));
}
