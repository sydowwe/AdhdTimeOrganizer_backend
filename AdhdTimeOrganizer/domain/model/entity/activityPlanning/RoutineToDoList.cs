using AdhdTimeOrganizer.domain.model.entity.@base;

namespace AdhdTimeOrganizer.domain.model.entity.activityPlanning;

public class RoutineTodoList : BaseEntityWithIsDone
{
    public int? DoneCount { get; set; }
    public int? TotalCount { get; set; }
    public required long TimePeriodId { get; set; }
    public RoutineTimePeriod RoutineTimePeriod { get; set; } = null!;
}