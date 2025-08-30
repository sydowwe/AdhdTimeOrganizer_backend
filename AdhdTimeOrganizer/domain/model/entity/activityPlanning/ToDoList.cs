using AdhdTimeOrganizer.domain.model.entity.@base;

namespace AdhdTimeOrganizer.domain.model.entity.activityPlanning;

public class ToDoList : BaseEntityWithIsDone
{
    public long TaskUrgencyId { get; set; }
    public TaskUrgency TaskUrgency { get; set; }
}