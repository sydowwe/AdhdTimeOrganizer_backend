using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.Core.application.dto.response.activity;
using AdhdTimeOrganizer.Core.application.dto.response.extendable;
using Sydowwe.Framework.application.dto.response;

namespace AdhdTimeOrganizer.ActivityProfiles.application.dto.response;

/// <inheritdoc cref="ActivityBacklogProfileResponse"/>
public record ActivityProjectProfileResponse : WithActivityResponse, IProjectionResponse<ActivityProjectProfileResponse, ActivityProjectProfile>
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
            Activity = new ActivityResponse
            {
                Id = e.Activity.Id,
                Name = e.Activity.Name,
                Text = e.Activity.Text,
                IsUnavoidable = e.Activity.IsUnavoidable,
                IsArchived = e.Activity.IsArchived,
                Role = new ActivityRoleResponse
                {
                    Id = e.Activity.Role.Id, Name = e.Activity.Role.Name, Text = e.Activity.Role.Text,
                    Color = e.Activity.Role.Color, Icon = e.Activity.Role.Icon
                },
                Category = e.Activity.Category == null
                    ? null
                    : new ActivityCategoryResponse
                    {
                        Id = e.Activity.Category.Id, Name = e.Activity.Category.Name, Text = e.Activity.Category.Text,
                        Color = e.Activity.Category.Color, Icon = e.Activity.Category.Icon
                    }
            },
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
