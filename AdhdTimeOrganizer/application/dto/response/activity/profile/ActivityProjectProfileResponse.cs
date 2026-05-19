using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.application.dto.response.activity.profile;

public record ActivityProjectProfileResponse : IdResponse
{
    public required long ActivityId { get; init; }
    public required DifficultyLevel DifficultyLevel { get; init; }
    public required string ProjectArea { get; init; }
    public required decimal EstimatedHours { get; init; }
    public required bool IsMessy { get; init; }
    public required List<string> MaterialsNeeded { get; init; }
    public required List<string> RequiredTools { get; init; }
    public required ReadinessStatus ReadinessStatus { get; init; }
}
