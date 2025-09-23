using AdhdTimeOrganizer.application.dto.response.extendable;

namespace AdhdTimeOrganizer.application.dto.response.activityPlanning;

public record RoutineTodoListResponse : BaseTodoListResponse
{
    public required RoutineTimePeriodResponse RoutineTimePeriod { get; init; }
}