using AdhdTimeOrganizer.Common.application.dto.response.@base;

namespace AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;

public record RoutineToDoListGroupedResponse : IMyResponse
{
    public required TimePeriodResponse TimePeriod { get; init; }
    public required IEnumerable<RoutineToDoListResponse> Items { get; init; }
}