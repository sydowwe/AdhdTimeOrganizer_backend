using AdhdTimeOrganizer.domain.model.entity.activity;

namespace AdhdTimeOrganizer.domain.model.entity.activityPlanning;

public abstract class BasePlannerTask : BaseEntityWithActivity
{
    public required TimeOnly StartTime { get; set; }
    public required TimeOnly EndTime { get; set; }
    public required bool IsBackground { get; set; }

    // Notes
    public string? Location { get; set; } // "Home", "Gym", "Office"
    public string? Notes { get; set; }

    public long? ImportanceId { get; set; }
    public TaskImportance? Importance { get; set; }

    public bool IsOptional => Importance?.Importance == TaskImportance.OptionalMarkerValue;
}