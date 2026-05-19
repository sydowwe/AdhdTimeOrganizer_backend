using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.application.dto.response.activity.profile;

public record ActivityBacklogProfileResponse : IdResponse
{
    public required long ActivityId { get; init; }
    public required long LocationTypeId { get; init; }
    public required long WeatherDependencyId { get; init; }
    public required EnergyLevel EnergyLevel { get; init; }
    public EffortType? EffortType { get; init; }
    public required int MinParticipants { get; init; }
    public int? MaxParticipants { get; init; }
    public required long ExpectedCostTierId { get; init; }
    public required int DurationMinutes { get; init; }
    public required bool IsRepeatable { get; init; }
}
