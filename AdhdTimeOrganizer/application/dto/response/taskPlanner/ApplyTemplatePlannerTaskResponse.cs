using AdhdTimeOrganizer.application.dto.response.taskPlanner.template;

namespace AdhdTimeOrganizer.application.dto.response.taskPlanner;

public record ApplyTemplatePlannerTaskResponse
{
    public required CalendarResponse Calendar { get; init; }
    public required List<PlannerTaskResponse> Tasks { get; init; }
}