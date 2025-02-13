using AdhdTimeOrganizer.Command.application.dto.response.extendable;

namespace AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;

public class PlannerTaskResponse : WithIsDoneResponse
{
    public DateTime StartTimestamp { get; set; }
    public int MinuteLength { get; set; }
    public string Color { get; set; }
}