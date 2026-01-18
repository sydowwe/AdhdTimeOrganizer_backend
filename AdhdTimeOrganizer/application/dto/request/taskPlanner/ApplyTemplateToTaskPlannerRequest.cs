using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.@enum;

namespace AdhdTimeOrganizer.application.dto.request.taskPlanner;

public record ApplyTemplateToTaskPlannerRequest
{
    [Required]
    public long TemplateId { get; init; }
    [Required]
    public required long CalendarId { get; init; }
    [Required]
    public required ApplyTemplateConflictResolutionEnum ConflictResolution { get; init; }
    [Required]
    public required List<PlannerTaskRequest> TasksFromTemplate { get; init; }
}