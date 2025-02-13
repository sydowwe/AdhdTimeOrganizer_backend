using AdhdTimeOrganizer.Common.application.dto.response.@base;

namespace AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;

public class TaskUrgencyResponse : TextColorResponse
{
    public int Priority { get; set; }
}