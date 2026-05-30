using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.domain.model.entity.activity.profile;

namespace AdhdTimeOrganizer.application.dto.request.activity.profile;

public record ActivityProjectProfileRequest : IMyRequest<ActivityProjectProfile>
{
    [Required] public long ActivityId { get; init; }
    [Required] public DifficultyLevel DifficultyLevel { get; init; }
    [Required] public string ProjectArea { get; init; } = null!;
    [Required] public decimal EstimatedHours { get; init; }
    [Required] public bool IsMessy { get; init; }
    public List<string> MaterialsNeeded { get; init; } = [];
    public List<string> RequiredTools { get; init; } = [];
    [Required] public ReadinessStatus ReadinessStatus { get; init; }

    public ActivityProjectProfile ToEntity => new()
    {
        ActivityId = ActivityId,
        DifficultyLevel = DifficultyLevel,
        ProjectArea = ProjectArea,
        EstimatedHours = EstimatedHours,
        IsMessy = IsMessy,
        MaterialsNeeded = MaterialsNeeded,
        RequiredTools = RequiredTools,
        ReadinessStatus = ReadinessStatus,
    };

    public void UpdateEntity(ActivityProjectProfile e)
    {
        e.DifficultyLevel = DifficultyLevel;
        e.ProjectArea = ProjectArea;
        e.EstimatedHours = EstimatedHours;
        e.IsMessy = IsMessy;
        e.MaterialsNeeded = MaterialsNeeded;
        e.RequiredTools = RequiredTools;
        e.ReadinessStatus = ReadinessStatus;
    }
}
