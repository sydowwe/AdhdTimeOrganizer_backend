using AdhdTimeOrganizer.Command.application.dto.response.activity;
using AdhdTimeOrganizer.Command.application.dto.response.extendable;

namespace AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;

public record RoutineToDoListResponse : WithIsDoneResponse
{
    public required TimePeriodResponse TimePeriod { get; init; }
}