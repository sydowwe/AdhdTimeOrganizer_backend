using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;

namespace AdhdTimeOrganizer.ActivityProfiles.application.dto.response;

public record ActivityBacklogProfileResponse : IdResponse, IProjectionResponse<ActivityBacklogProfileResponse, ActivityBacklogProfile>
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

    public static IQueryable<ActivityBacklogProfileResponse> Projection(IQueryable<ActivityBacklogProfile> query)
    {
        return query.Select(e => new ActivityBacklogProfileResponse
        {
            Id = e.Id,
            ActivityId = e.ActivityId,
            LocationTypeId = e.LocationTypeId,
            WeatherDependencyId = e.WeatherDependencyId,
            EnergyLevel = e.EnergyLevel,
            EffortType = e.EffortType,
            MinParticipants = e.MinParticipants,
            MaxParticipants = e.MaxParticipants,
            ExpectedCostTierId = e.ExpectedCostTierId,
            DurationMinutes = e.DurationMinutes,
            IsRepeatable = e.IsRepeatable
        });
    }
}