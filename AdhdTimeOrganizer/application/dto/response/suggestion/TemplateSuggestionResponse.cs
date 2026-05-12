using AdhdTimeOrganizer.application.dto.response.taskPlanner.template;

namespace AdhdTimeOrganizer.application.dto.response.suggestion;

public record TemplateSuggestionResponse
{
    public required TaskPlannerDayTemplateResponse Template { get; init; }
    public required int PatternType { get; init; }    // 0=DayOfWeek, 1=DayType
    public required string PatternLabel { get; init; } // e.g., "Monday" or "Workday"
    public required int OccurrenceCount { get; init; }
}
