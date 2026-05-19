using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.application.dto.filter;

public class ActivityProjectProfileFilterRequest : IFilterRequest
{
    public DifficultyLevel? DifficultyLevel { get; set; }
    public ReadinessStatus? ReadinessStatus { get; set; }
    public bool? IsMessy { get; set; }
    public string? ProjectArea { get; set; }
}
