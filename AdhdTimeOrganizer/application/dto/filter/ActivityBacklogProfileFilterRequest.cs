using AdhdTimeOrganizer.domain.model.@enum;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.filter;

public record ActivityBacklogProfileFilterRequest : IFilterRequest
{
    public EnergyLevel? EnergyLevel { get; set; }
    public EffortType? EffortType { get; set; }
    public bool? IsRepeatable { get; set; }
}