using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner.template;

namespace AdhdTimeOrganizer.Planning.application.dto.response.suggestion;

public record TemplateSuggestionResponse
{
    public required TaskPlannerDayTemplateResponse Template { get; init; }
    // History (applied before): 0=DayOfWeek, 1=DayType. Template metadata, no history: 2=ScheduledDays, 3=SuggestedForDayType.
    public required int PatternType { get; init; }
    public required string PatternLabel { get; init; } // Invariant DayOfWeek / DayType member name, e.g. "Monday" or "Workday"
    public required int OccurrenceCount { get; init; } // 0 for pattern types 2 and 3
}