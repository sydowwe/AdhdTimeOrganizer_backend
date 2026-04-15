
namespace AdhdTimeOrganizer.domain.model.entity.todoList;

public class RoutinePeriodCompletion : BaseEntity
{
    public required long TimePeriodId { get; set; }
    public RoutineTimePeriod RoutineTimePeriod { get; set; } = null!;
    public required DateOnly PeriodStart { get; set; }
    public required DateOnly PeriodEnd { get; set; }
    public required int CompletedCount { get; set; }
    public required int TotalCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
