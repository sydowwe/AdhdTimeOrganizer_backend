using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.domain.model.entity.activity.profile;

namespace AdhdTimeOrganizer.application.dto.request.activity.profile;

public record ActivityBacklogProfileRequest : IMyRequest<ActivityBacklogProfile>
{
    [Required] public long ActivityId { get; init; }
    [Required] public long LocationTypeId { get; init; }
    [Required] public long WeatherDependencyId { get; init; }
    [Required] public EnergyLevel EnergyLevel { get; init; }
    public EffortType? EffortType { get; init; }
    [Required] public int MinParticipants { get; init; }
    public int? MaxParticipants { get; init; }
    [Required] public long ExpectedCostTierId { get; init; }
    [Required] public int DurationMinutes { get; init; }
    [Required] public bool IsRepeatable { get; init; }

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
        IsRepeatable = IsRepeatable,
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
