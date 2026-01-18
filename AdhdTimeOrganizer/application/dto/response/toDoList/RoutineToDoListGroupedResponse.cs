using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.toDoList;

public record RoutineTodoListGroupedResponse : IMyResponse
{
    public required RoutineTimePeriodResponse RoutineTimePeriod { get; init; }
    public required IEnumerable<RoutineTodoListResponse> Items { get; init; }
}