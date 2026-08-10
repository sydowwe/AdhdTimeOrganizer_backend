using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using Sydowwe.Framework.application.dto.dto;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;

public record CalendarRequest : IUpdateRequest<Calendar>
{
    public required DayType DayType { get; init; }
    public string? Label { get; init; }

    public required TimeDto WakeUpTime { get; init; }
    public required TimeDto BedTime { get; init; }

    public Location? Location { get; init; }

    public string? Weather { get; init; }
    public string? Notes { get; init; }

    public void UpdateEntity(Calendar entity)
    {
        entity.DayType = DayType;
        entity.Label = Label;
        entity.WakeUpTime = WakeUpTime.ToTimeOnly();
        entity.BedTime = BedTime.ToTimeOnly();
        entity.Location = Location;
        entity.Weather = Weather;
        entity.Notes = Notes;
    }
}