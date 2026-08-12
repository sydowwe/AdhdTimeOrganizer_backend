using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Sydowwe.Framework.domain.entity.@base;

namespace AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;

public class ActivityBacklogProfile : BaseTableEntity
{
    public long ActivityId { get; set; }
    public Activity Activity { get; set; } = null!;

    public long LocationTypeId { get; set; }
    public ActivityLocationType LocationType { get; set; } = null!;
    public long WeatherDependencyId { get; set; }
    public ActivityWeatherDependency WeatherDependency { get; set; } = null!;
    public EnergyLevel EnergyLevel { get; set; }
    public EffortType? EffortType { get; set; }
    public int MinParticipants { get; set; }
    public int? MaxParticipants { get; set; }
    public long ExpectedCostTierId { get; set; }
    public ActivityExpectedCostTier ExpectedCostTier { get; set; } = null!;
    public int DurationMinutes { get; set; }
    public bool IsRepeatable { get; set; }
}