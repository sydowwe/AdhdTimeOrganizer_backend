using AdhdTimeOrganizer.Core.domain.model.@enum;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.Core.application.dto.filter;

public record ActivityProjectProfileFilterRequest : IFilterRequest
{
    public DifficultyLevel? DifficultyLevel { get; set; }
    public ReadinessStatus? ReadinessStatus { get; set; }
    public bool? IsMessy { get; set; }
    public string? ProjectArea { get; set; }
}