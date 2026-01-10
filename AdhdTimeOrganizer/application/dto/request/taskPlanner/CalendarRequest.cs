using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.application.dto.response.activityPlanning;

public record CalendarRequest : IUpdateRequest
{
    public required DayType DayType { get; init; }
    public string? Label { get; init; }

    public required TimeDto WakeUpTime { get; init; }
    public required TimeDto BedTime { get; init; }

    public string? Weather { get; init; }
    public string? Notes { get; init; }

}