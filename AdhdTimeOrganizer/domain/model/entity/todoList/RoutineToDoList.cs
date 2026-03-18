namespace AdhdTimeOrganizer.domain.model.entity.todoList;

public class RoutineTodoList : BaseTodoList
{
    public required long TimePeriodId { get; set; }
    public RoutineTimePeriod RoutineTimePeriod { get; set; } = null!;
    public DateOnly? LastResetDate { get; set; }
    public int Streak { get; set; }
    public int BestStreak { get; set; }
    public DateTime LastCompletedAt { get; set; }
}