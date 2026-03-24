using AdhdTimeOrganizer.application.dto.response.taskPlanner;

namespace AdhdTimeOrganizer.application.dto.response.toDoList;

public record TodoListItemResponse : BaseTodoListResponse
{
    public required TaskPriorityResponse TaskPriority { get; init; }
    public DateOnly? DueDate { get; init; }
    public TimeOnly? DueTime { get; init; }
}
