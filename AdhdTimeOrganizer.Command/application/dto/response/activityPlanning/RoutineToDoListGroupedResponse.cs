using AdhdTimeOrganizer.Common.application.dto.response.@base;

namespace AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;

public class RoutineToDoListGroupedResponse : IMyResponse
{
    public TimePeriodResponse TimePeriod { get; set; }
    public IEnumerable<RoutineToDoListResponse> Items { get; set; }

    public RoutineToDoListGroupedResponse(TimePeriodResponse timePeriod, IEnumerable<RoutineToDoListResponse> items)
    {
        this.TimePeriod = timePeriod;
        this.Items = items;
    }
}