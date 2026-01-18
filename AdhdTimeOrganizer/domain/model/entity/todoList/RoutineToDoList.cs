namespace AdhdTimeOrganizer.domain.model.entity.todoList;

public class RoutineTodoList : BaseTodoList
{
    public required long TimePeriodId { get; set; }
    public RoutineTimePeriod RoutineTimePeriod { get; set; } = null!;
}