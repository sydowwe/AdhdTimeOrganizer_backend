using AdhdTimeOrganizer.Command.application.dto.response.extendable;

namespace AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;

public class ToDoListResponse : WithIsDoneResponse
{
    public TaskUrgencyResponse taskUrgency { get; set; }
}