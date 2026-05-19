using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.application.dto.request.activity.profile;

public record ActivityBacklogProfileRequest : ICreateRequest, IUpdateRequest
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
}
