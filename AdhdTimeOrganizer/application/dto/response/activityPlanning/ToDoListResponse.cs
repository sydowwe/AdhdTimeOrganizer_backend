using AdhdTimeOrganizer.application.dto.response.extendable;

namespace AdhdTimeOrganizer.application.dto.response.activityPlanning;

public record ToDoListResponse : WithIsDoneResponse
{
    public required TaskUrgencyResponse TaskUrgency { get; init; }
}