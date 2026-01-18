using AdhdTimeOrganizer.application.dto.response.taskPlanner;

namespace AdhdTimeOrganizer.application.dto.response.toDoList;

public record TodoListResponse : BaseTodoListResponse
{
    public required TaskPriorityResponse TaskPriority { get; init; }
}