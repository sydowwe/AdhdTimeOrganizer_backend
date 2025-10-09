using AdhdTimeOrganizer.domain.model.entity.@base;

namespace AdhdTimeOrganizer.domain.model.entity.activityPlanning;

public class TaskPriority : BaseTextColorEntity
{
    public int Priority { get; set; }
    public ICollection<TodoList> TodoListColl { get; set; } = new List<TodoList>();
}