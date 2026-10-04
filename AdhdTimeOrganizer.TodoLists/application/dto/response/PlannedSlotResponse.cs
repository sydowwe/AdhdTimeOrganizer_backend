using AdhdTimeOrganizer.Core.application.seam;
using Sydowwe.Framework.application.dto.dto;

namespace AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;

/// <summary>When a to-do item is next planned: the planner day and the block's start on it.</summary>
public record PlannedSlotResponse
{
    public required DateOnly Date { get; init; }
    public required TimeDto StartTime { get; init; }

    public static PlannedSlotResponse From(PlannedSlot slot) => new()
    {
        Date = slot.Date,
        StartTime = new TimeDto(slot.StartTime.Hour, slot.StartTime.Minute)
    };
}
