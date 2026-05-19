using AdhdTimeOrganizer.domain.model.entity.@base.core;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.domain.model.entity.activity.profile;

public class ActivityProjectProfile : BaseTableEntity
{
    public long ActivityId { get; set; }
    public Activity Activity { get; set; } = null!;

    public DifficultyLevel DifficultyLevel { get; set; }
    public string ProjectArea { get; set; } = null!;
    public decimal EstimatedHours { get; set; }
    public bool IsMessy { get; set; }
    public List<string> MaterialsNeeded { get; set; } = [];
    public List<string> RequiredTools { get; set; } = [];
    public ReadinessStatus ReadinessStatus { get; set; }
}
