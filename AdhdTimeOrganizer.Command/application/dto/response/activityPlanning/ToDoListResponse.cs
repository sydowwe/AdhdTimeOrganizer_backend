using AdhdTimeOrganizer.Command.application.dto.response.activity;
using AdhdTimeOrganizer.Command.application.dto.response.extendable;

namespace AdhdTimeOrganizer.Command.application.dto.response.activityPlanning;

public record ToDoListResponse : WithIsDoneResponse
{
    public required TaskUrgencyResponse TaskUrgency { get; init; }
}