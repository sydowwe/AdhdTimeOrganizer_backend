using AdhdTimeOrganizer.domain.model.entity.@base;

namespace AdhdTimeOrganizer.domain.model.entity.activityPlanning;

public class TaskImportance : BaseTextColorEntity
{
    public int Importance { get; set; }
}