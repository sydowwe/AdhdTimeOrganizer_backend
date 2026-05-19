using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.application.dto.request.activity.profile;

public record ActivityProjectProfileRequest : ICreateRequest, IUpdateRequest
{
    [Required] public long ActivityId { get; init; }
    [Required] public DifficultyLevel DifficultyLevel { get; init; }
    [Required] public string ProjectArea { get; init; } = null!;
    [Required] public decimal EstimatedHours { get; init; }
    [Required] public bool IsMessy { get; init; }
    public List<string> MaterialsNeeded { get; init; } = [];
    public List<string> RequiredTools { get; init; } = [];
    [Required] public ReadinessStatus ReadinessStatus { get; init; }
}
