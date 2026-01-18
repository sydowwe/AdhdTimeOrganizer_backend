using AdhdTimeOrganizer.domain.model.entity.@base;

namespace AdhdTimeOrganizer.domain.model.entity.activityPlanning;

public class TaskImportance : BaseTextColorIconEntity
{
    public required int Importance { get; set; }
}