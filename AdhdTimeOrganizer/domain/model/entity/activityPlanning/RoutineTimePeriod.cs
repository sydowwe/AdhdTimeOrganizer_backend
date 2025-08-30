using AdhdTimeOrganizer.domain.model.entity.@base;

namespace AdhdTimeOrganizer.domain.model.entity.activityPlanning;

public class RoutineTimePeriod : BaseTextColorEntity
{
    public required int LengthInDays { get; set; }
    public bool IsHiddenInView { get; set; }
    public ICollection<RoutineTodoList> RoutineTodoListColl { get; set; } = new List<RoutineTodoList>();
}