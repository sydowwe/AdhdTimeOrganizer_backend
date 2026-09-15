using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.ActivityProfiles.application.dto.filter;

/// <inheritdoc cref="ActivityBacklogProfileFilterRequest" path="/summary"/>
public record ActivityProjectProfileFilterRequest : IFilterRequest
{
    /// <summary>Case-insensitive contains on the activity's name.</summary>
    public string? ActivityName { get; set; }

    /// <summary>Any-of. The client's chips; <see cref="DifficultyLevel"/> is the older exact-match scalar.</summary>
    public DifficultyLevel[]? DifficultyLevels { get; set; }

    /// <inheritdoc cref="DifficultyLevels"/>
    public ReadinessStatus[]? ReadinessStatuses { get; set; }

    public DifficultyLevel? DifficultyLevel { get; set; }
    public ReadinessStatus? ReadinessStatus { get; set; }
    public bool? IsMessy { get; set; }
    public string? ProjectArea { get; set; }
}
