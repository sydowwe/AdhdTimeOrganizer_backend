using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.application.dto.response.taskPlanner.template;

public record TaskPlannerDayTemplateResponse : IdResponse
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Icon { get; init; }
    public required bool IsActive { get; init; }
    public TimeDto? DefaultWakeUpTime { get; init; }
    public TimeDto? DefaultBedTime { get; init; }
    public required int UsageCount { get; init; }
    public DateTimeOffset? LastUsedAt { get; init; }
    public required DayType SuggestedForDayType { get; init; }
    public required List<string> Tags { get; init; }
}
