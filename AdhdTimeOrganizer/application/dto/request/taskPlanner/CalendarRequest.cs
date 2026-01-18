using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.application.dto.request.taskPlanner;

public record CalendarRequest : IUpdateRequest
{
    public required DayType DayType { get; init; }
    public string? Label { get; init; }

    public required TimeDto WakeUpTime { get; init; }
    public required TimeDto BedTime { get; init; }

    public string? Weather { get; init; }
    public string? Notes { get; init; }

}