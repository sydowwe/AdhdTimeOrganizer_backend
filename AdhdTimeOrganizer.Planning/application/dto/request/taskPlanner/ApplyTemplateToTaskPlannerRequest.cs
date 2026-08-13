using AdhdTimeOrganizer.Planning.application.dto.@enum;

namespace AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;

public record ApplyTemplateToTaskPlannerRequest
{
    public long TemplateId { get; init; }

    /// <summary>The day to apply the template to. Exactly one of this and <see cref="Date"/> is required.</summary>
    public long? CalendarId { get; init; }

    /// <summary>
    /// The day to apply the template to, named by date — the calendar row is created if the user has none.
    /// Applying a day template is the other way a date gets its first task, so it lazily creates the day for
    /// the same reason <c>CreatePlannerTaskEndpoint</c> does.
    /// </summary>
    public DateOnly? Date { get; init; }

    public required ApplyTemplateConflictResolutionEnum ConflictResolution { get; init; }

    public required List<PlannerTaskRequest> TasksFromTemplate { get; init; }
}