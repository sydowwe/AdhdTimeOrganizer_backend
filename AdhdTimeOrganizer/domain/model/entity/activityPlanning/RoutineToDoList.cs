using AdhdTimeOrganizer.domain.model.entity.@base;
using AdhdTimeOrganizer.domain.model.entityInterface;

namespace AdhdTimeOrganizer.domain.model.entity.activityPlanning;

public class RoutineTodoList : BaseTodoList
{
    public required long TimePeriodId { get; set; }
    public RoutineTimePeriod RoutineTimePeriod { get; set; } = null!;
}