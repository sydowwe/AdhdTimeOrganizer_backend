using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.toDoList;

public record RoutineTimePeriodResponse : TextColorResponse
{
    public required int LengthInDays { get; init; }
    public bool IsHidden { get; init; } = false;
}