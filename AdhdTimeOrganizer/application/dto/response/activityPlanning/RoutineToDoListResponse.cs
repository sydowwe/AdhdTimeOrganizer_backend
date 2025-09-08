using AdhdTimeOrganizer.application.dto.response.extendable;

namespace AdhdTimeOrganizer.application.dto.response.activityPlanning;

public record RoutineTodoListResponse : WithIsDoneResponse
{
    public int? DoneCount { get; set; }
    public int? TotalCount { get; set; }
    public required RoutineTimePeriodResponse RoutineTimePeriod { get; init; }
}