namespace AdhdTimeOrganizer.application.dto.response.toDoList;

public record RoutineTodoListResponse : BaseTodoListResponse
{
    public required RoutineTimePeriodResponse RoutineTimePeriod { get; init; }
}