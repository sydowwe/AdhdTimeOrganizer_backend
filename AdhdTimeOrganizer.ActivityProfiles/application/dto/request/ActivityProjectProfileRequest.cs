using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.ActivityProfiles.application.dto.request;

public record ActivityProjectProfileRequest : IMyRequest<ActivityProjectProfile>
{
    public long ActivityId { get; init; }
    public DifficultyLevel DifficultyLevel { get; init; }
    public string ProjectArea { get; init; } = null!;
    public decimal EstimatedHours { get; init; }
    public bool IsMessy { get; init; }
    public List<string> MaterialsNeeded { get; init; } = [];
    public List<string> RequiredTools { get; init; } = [];
    public ReadinessStatus ReadinessStatus { get; init; }

    public ActivityProjectProfile ToEntity => new()
    {
        ActivityId = ActivityId,
        DifficultyLevel = DifficultyLevel,
        ProjectArea = ProjectArea,
        EstimatedHours = EstimatedHours,
        IsMessy = IsMessy,
        MaterialsNeeded = MaterialsNeeded,
        RequiredTools = RequiredTools,
        ReadinessStatus = ReadinessStatus
    };

    /// <remarks>
    /// Deliberately leaves <see cref="ActivityProjectProfile.ShoppingTodoListItemId"/> alone: the form does not
    /// carry it, so assigning it here would unlink the shopping item on every edit.
    /// </remarks>
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