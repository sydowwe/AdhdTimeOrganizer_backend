using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.application.dto.filter;

public class ActivityBacklogProfileFilterRequest : IFilterRequest
{
    public EnergyLevel? EnergyLevel { get; set; }
    public EffortType? EffortType { get; set; }
    public bool? IsRepeatable { get; set; }
}
