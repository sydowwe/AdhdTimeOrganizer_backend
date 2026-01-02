using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.application.dto.request.taskPlannerDayTemplate;

public record TaskPlannerDayTemplateRequest : IMyRequest
{
    [Required, StringLength(100)]
    public required string Name { get; init; }

    [StringLength(500)]
    public string? Description { get; init; }

    [StringLength(50)]
    public string? Icon { get; init; }

    [Required]
    public required bool IsActive { get; init; }

    public TimeDto? DefaultWakeUpTime { get; init; }

    public TimeDto? DefaultBedTime { get; init; }

    [Required]
    public required DayType SuggestedForDayType { get; init; }

    public List<string> Tags { get; init; } = new();
}
