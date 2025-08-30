using AdhdTimeOrganizer.domain.model.entity.@base;

namespace AdhdTimeOrganizer.domain.model.entity.activityPlanning;

public class PlannerTask : BaseEntityWithIsDone
{
    public required DateTime StartTimestamp { get; set; }
    public required int MinuteLength { get; set; }
    public required string Color { get; set; } = "#1A237E";
}