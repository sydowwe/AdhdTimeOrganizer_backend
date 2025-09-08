using AdhdTimeOrganizer.domain.model.entity.@base;

namespace AdhdTimeOrganizer.domain.model.entity.activityPlanning;

public class TodoList : BaseEntityWithIsDone
{
    public int? DoneCount { get; set; }
    public int? TotalCount { get; set; }
    public required long TaskUrgencyId { get; set; }
    public TaskUrgency TaskUrgency { get; set; } = null!;
}