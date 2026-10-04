using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.TodoLists.application.service;

/// <summary>
/// Overlays <see cref="TodoListItemResponse.NextPlannedSlot"/> onto already-projected items, from the planner via
/// <see cref="ITodoListItemPlanSource"/>. One query per call however many items, and "today" is the user's — a
/// block planned for today stays "scheduled" all day on their clock, not until UTC's midnight.
/// <para>
/// Static rather than a service: the lifetime-marker scan registers by implemented interface only, and an
/// interface with one implementation and two callers would be ceremony. The endpoints inject the two
/// collaborators themselves.
/// </para>
/// </summary>
public static class TodoListItemPlannedSlotFiller
{
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

        var today = DateOnly.FromDateTime(WallClockZone.FromUtc(DateTime.UtcNow, await timeZones.GetAsync(userId, ct)));
        var slots = await planSource.NextPlannedSlotsAsync(dbContext, userId, items.Select(i => i.Id).ToList(), today, ct);

        return items
            .Select(i => slots.TryGetValue(i.Id, out var slot) ? i with { NextPlannedSlot = PlannedSlotResponse.From(slot) } : i)
            .ToList();
    }
}
