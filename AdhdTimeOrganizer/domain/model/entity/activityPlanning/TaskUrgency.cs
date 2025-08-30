using AdhdTimeOrganizer.domain.model.entity.@base;

namespace AdhdTimeOrganizer.domain.model.entity.activityPlanning;

public class TaskUrgency : BaseTextColorEntity
{
    public int Priority { get; set; }
    public ICollection<ToDoList> ToDoListColl { get; set; } = new List<ToDoList>();
}