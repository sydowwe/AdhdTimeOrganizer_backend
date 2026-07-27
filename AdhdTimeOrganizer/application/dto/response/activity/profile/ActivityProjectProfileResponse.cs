using AdhdTimeOrganizer.domain.model.entity.activity.profile;
using AdhdTimeOrganizer.domain.model.@enum;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.activity.profile;

public record ActivityProjectProfileResponse : IdResponse, IProjectionResponse<ActivityProjectProfileResponse, ActivityProjectProfile>
{
    public required long ActivityId { get; init; }
    public required DifficultyLevel DifficultyLevel { get; init; }
    public required string ProjectArea { get; init; }
    public required decimal EstimatedHours { get; init; }
    public required bool IsMessy { get; init; }
    public required List<string> MaterialsNeeded { get; init; }
    public required List<string> RequiredTools { get; init; }
    public required ReadinessStatus ReadinessStatus { get; init; }

    public static IQueryable<ActivityProjectProfileResponse> Projection(IQueryable<ActivityProjectProfile> query)
    {
        return query.Select(e => new ActivityProjectProfileResponse
        {
            Id = e.Id,
            ActivityId = e.ActivityId,
            DifficultyLevel = e.DifficultyLevel,
            ProjectArea = e.ProjectArea,
            EstimatedHours = e.EstimatedHours,
            IsMessy = e.IsMessy,
            MaterialsNeeded = e.MaterialsNeeded,
            RequiredTools = e.RequiredTools,
            ReadinessStatus = e.ReadinessStatus
        });
    }
}