using AdhdTimeOrganizer.application.dto.@enum;

namespace AdhdTimeOrganizer.application.dto.request.taskPlanner;

public record ApplyTemplateToTaskPlannerRequest
{
    public long TemplateId { get; init; }

    public required long CalendarId { get; init; }

    public required ApplyTemplateConflictResolutionEnum ConflictResolution { get; init; }

    public required List<PlannerTaskRequest> TasksFromTemplate { get; init; }
}