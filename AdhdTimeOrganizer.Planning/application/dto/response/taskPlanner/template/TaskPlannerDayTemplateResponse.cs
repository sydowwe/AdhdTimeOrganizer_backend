using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.dto.dto;
using Sydowwe.Framework.application.dto.response;
using Sydowwe.Framework.application.dto.response.@base;
using Sydowwe.Framework.application.dto.response.generic;

namespace AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner.template;

public record TaskPlannerDayTemplateResponse : IdResponse, IProjectionResponse<TaskPlannerDayTemplateResponse, TaskPlannerDayTemplate>
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Icon { get; init; }
    public required bool IsActive { get; init; }
    public required bool IsPinned { get; init; }
    public TimeDto? DefaultWakeUpTime { get; init; }
    public TimeDto? DefaultBedTime { get; init; }
    public required int UsageCount { get; init; }
    public DateTimeOffset? LastUsedAt { get; init; }
    public required DayType SuggestedForDayType { get; init; }
    public required List<DayOfWeek> ScheduledDays { get; init; }
    public Location? SuggestedLocation { get; init; }
    public required List<string> Tags { get; init; }

    public static IQueryable<TaskPlannerDayTemplateResponse> Projection(IQueryable<TaskPlannerDayTemplate> query)
    {
        return query.Select(t => new TaskPlannerDayTemplateResponse
        {
            Id = t.Id,
            Name = t.Name,
            Description = t.Description,
            Icon = t.Icon,
            IsActive = t.IsActive,
            IsPinned = t.IsPinned,
            DefaultWakeUpTime = t.DefaultWakeUpTime != null ? new TimeDto(t.DefaultWakeUpTime.Value.Hour, t.DefaultWakeUpTime.Value.Minute) : null,
            DefaultBedTime = t.DefaultBedTime != null ? new TimeDto(t.DefaultBedTime.Value.Hour, t.DefaultBedTime.Value.Minute) : null,
            UsageCount = t.UsageCount,
            LastUsedAt = t.LastUsedAt,
            SuggestedForDayType = t.SuggestedForDayType,
            ScheduledDays = t.ScheduledDays,
            SuggestedLocation = t.SuggestedLocation,
            Tags = t.Tags
        });
    }

    public static IQueryable<SelectOptionResponse> SelectOptionProjection(IQueryable<TaskPlannerDayTemplate> query)
    {
        return query.Select(t => new SelectOptionResponse { Id = t.Id, Text = t.Name });
    }
}