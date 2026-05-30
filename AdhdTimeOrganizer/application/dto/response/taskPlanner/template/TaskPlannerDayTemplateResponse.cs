using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;

namespace AdhdTimeOrganizer.application.dto.response.taskPlanner.template;

public record TaskPlannerDayTemplateResponse : IdResponse, IProjectionResponse<TaskPlannerDayTemplateResponse, TaskPlannerDayTemplate>
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
    public required List<DayOfWeek> ScheduledDays { get; init; }
    public Location? SuggestedLocation { get; init; }
    public required List<string> Tags { get; init; }

    public static IQueryable<TaskPlannerDayTemplateResponse> Projection(IQueryable<TaskPlannerDayTemplate> query) =>
        query.Select(t => new TaskPlannerDayTemplateResponse
        {
            Id = t.Id,
            Name = t.Name,
            Description = t.Description,
            Icon = t.Icon,
            IsActive = t.IsActive,
            DefaultWakeUpTime = t.DefaultWakeUpTime != null ? new TimeDto(t.DefaultWakeUpTime.Value.Hour, t.DefaultWakeUpTime.Value.Minute) : null,
            DefaultBedTime = t.DefaultBedTime != null ? new TimeDto(t.DefaultBedTime.Value.Hour, t.DefaultBedTime.Value.Minute) : null,
            UsageCount = t.UsageCount,
            LastUsedAt = t.LastUsedAt,
            SuggestedForDayType = t.SuggestedForDayType,
            ScheduledDays = t.ScheduledDays,
            SuggestedLocation = t.SuggestedLocation,
            Tags = t.Tags
        });

    public static TaskPlannerDayTemplateResponse FromEntity(TaskPlannerDayTemplate entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Description = entity.Description,
        Icon = entity.Icon,
        IsActive = entity.IsActive,
        DefaultWakeUpTime = entity.DefaultWakeUpTime != null ? new TimeDto(entity.DefaultWakeUpTime.Value.Hour, entity.DefaultWakeUpTime.Value.Minute) : null,
        DefaultBedTime = entity.DefaultBedTime != null ? new TimeDto(entity.DefaultBedTime.Value.Hour, entity.DefaultBedTime.Value.Minute) : null,
        UsageCount = entity.UsageCount,
        LastUsedAt = entity.LastUsedAt,
        SuggestedForDayType = entity.SuggestedForDayType,
        ScheduledDays = entity.ScheduledDays,
        SuggestedLocation = entity.SuggestedLocation,
        Tags = entity.Tags
    };

    public static IQueryable<SelectOptionResponse> SelectOptionProjection(IQueryable<TaskPlannerDayTemplate> query) =>
        query.Select(t => new SelectOptionResponse { Id = t.Id, Text = t.Name });
}
