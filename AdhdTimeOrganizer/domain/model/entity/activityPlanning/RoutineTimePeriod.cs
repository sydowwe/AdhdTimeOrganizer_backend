using AdhdTimeOrganizer.domain.model.entity.@base;

namespace AdhdTimeOrganizer.domain.model.entity.activityPlanning;

public class RoutineTimePeriod : BaseTextColorEntity
{
    public required int LengthInDays { get; set; }
    public bool IsHiddenInView { get; set; }
    public ICollection<RoutineToDoList> RoutineToDoListColl { get; set; } = new List<RoutineToDoList>();
}