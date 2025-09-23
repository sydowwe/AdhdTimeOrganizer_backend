using AdhdTimeOrganizer.domain.model.entity.@base;
using AdhdTimeOrganizer.domain.model.entityInterface;

namespace AdhdTimeOrganizer.domain.model.entity.activityPlanning;

public class TodoList : BaseTodoList
{

    public required long TaskUrgencyId { get; set; }
    public TaskUrgency TaskUrgency { get; set; } = null!;
}