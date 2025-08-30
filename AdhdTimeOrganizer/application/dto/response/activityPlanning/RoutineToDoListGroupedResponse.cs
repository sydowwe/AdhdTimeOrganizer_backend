using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.activityPlanning;

public record RoutineToDoListGroupedResponse : IMyResponse
{
    public required RoutineTimePeriodResponse RoutineTimePeriod { get; init; }
    public required IEnumerable<RoutineToDoListResponse> Items { get; init; }
}