using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;

namespace AdhdTimeOrganizer.application.dto.request.taskPlanner.template;

public record TaskPlannerDayTemplateRequest : IMyRequest<TaskPlannerDayTemplate>
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

    public List<DayOfWeek> ScheduledDays { get; init; } = new();

    public Location? SuggestedLocation { get; init; }

    public List<string> Tags { get; init; } = new();

    public TaskPlannerDayTemplate ToEntity => new()
    {
        Name = Name,
        Description = Description,
        Icon = Icon,
        IsActive = IsActive,
        DefaultWakeUpTime = DefaultWakeUpTime?.ToTimeOnly(),
        DefaultBedTime = DefaultBedTime?.ToTimeOnly(),
        SuggestedForDayType = SuggestedForDayType,
        ScheduledDays = ScheduledDays,
        SuggestedLocation = SuggestedLocation,
        Tags = Tags
    };

    public void UpdateEntity(TaskPlannerDayTemplate entity)
    {
        entity.Name = Name;
        entity.Description = Description;
        entity.Icon = Icon;
        entity.IsActive = IsActive;
        entity.DefaultWakeUpTime = DefaultWakeUpTime?.ToTimeOnly();
        entity.DefaultBedTime = DefaultBedTime?.ToTimeOnly();
        entity.SuggestedForDayType = SuggestedForDayType;
        entity.ScheduledDays = ScheduledDays;
        entity.SuggestedLocation = SuggestedLocation;
        entity.Tags = Tags;
    }
}
