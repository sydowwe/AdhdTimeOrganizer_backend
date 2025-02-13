using AdhdTimeOrganizer.Command.application.dto.response.extendable;

namespace AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;

public class RoutineToDoListResponse : WithIsDoneResponse
{
    public TimePeriodResponse timePeriod { get; set; }
}