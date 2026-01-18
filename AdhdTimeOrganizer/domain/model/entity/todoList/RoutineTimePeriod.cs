using AdhdTimeOrganizer.domain.model.entity.@base;
using AdhdTimeOrganizer.domain.model.entityInterface;

namespace AdhdTimeOrganizer.domain.model.entity.todoList;

public class RoutineTimePeriod : BaseTextColorEntity, IEntityWithIsHidden
{
    public required int LengthInDays { get; set; }
    public bool IsHidden { get; set; }
    public ICollection<RoutineTodoList> RoutineTodoListColl { get; set; } = new List<RoutineTodoList>();
}