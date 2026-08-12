using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.ActivityProfiles.application.dto.request;

public record ActivityBacklogProfileRequest : IMyRequest<ActivityBacklogProfile>
{
    public long ActivityId { get; init; }
    public long LocationTypeId { get; init; }
    public long WeatherDependencyId { get; init; }
    public EnergyLevel EnergyLevel { get; init; }
    public EffortType? EffortType { get; init; }
    public int MinParticipants { get; init; }
    public int? MaxParticipants { get; init; }
    public long ExpectedCostTierId { get; init; }
    public int DurationMinutes { get; init; }
    public bool IsRepeatable { get; init; }

    public ActivityBacklogProfile ToEntity => new()
    {
        ActivityId = ActivityId,
        LocationTypeId = LocationTypeId,
        WeatherDependencyId = WeatherDependencyId,
        EnergyLevel = EnergyLevel,
        EffortType = EffortType,
        MinParticipants = MinParticipants,
        MaxParticipants = MaxParticipants,
        ExpectedCostTierId = ExpectedCostTierId,
        DurationMinutes = DurationMinutes,
        IsRepeatable = IsRepeatable
    };

    public void UpdateEntity(ActivityBacklogProfile e)
    {
        e.LocationTypeId = LocationTypeId;
        e.WeatherDependencyId = WeatherDependencyId;
        e.EnergyLevel = EnergyLevel;
        e.EffortType = EffortType;
        e.MinParticipants = MinParticipants;
        e.MaxParticipants = MaxParticipants;
        e.ExpectedCostTierId = ExpectedCostTierId;
        e.DurationMinutes = DurationMinutes;
        e.IsRepeatable = IsRepeatable;
    }
}