using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.application.dto.response.taskPlanner;

public record CalendarResponse : IdResponse
{
    public required DateOnly Date { get; init; }

    public required int DayIndex { get; init; }
    public required DayType DayType { get; init; }
    public string? HolidayName { get; init; }
    public string? Label { get; init; }

    public required TimeDto WakeUpTime { get; init; }
    public required TimeDto BedTime { get; init; }

    public long? AppliedTemplateId { get; init; }
    public string? AppliedTemplateName { get; init; }

    public string? Weather { get; init; }
    public string? Notes { get; init; }

    public required int TotalTasks { get; init; }
    public required int CompletedTasks { get; init; }
}