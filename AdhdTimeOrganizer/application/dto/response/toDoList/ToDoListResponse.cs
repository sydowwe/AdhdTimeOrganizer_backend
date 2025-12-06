using AdhdTimeOrganizer.application.dto.response.extendable;

namespace AdhdTimeOrganizer.application.dto.response.activityPlanning;

public record TodoListResponse : BaseTodoListResponse
{
    public required TaskPriorityResponse TaskPriority { get; init; }
}