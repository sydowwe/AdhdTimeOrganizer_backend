namespace AdhdTimeOrganizer.application.dto.response.toDoList;

public record RoutineTodoListResponse : BaseTodoListResponse
{
    public required RoutineTimePeriodResponse RoutineTimePeriod { get; init; }
    public int Streak { get; init; }
    public int BestStreak { get; init; }
    public DateTime LastCompletedAt { get; init; }
}