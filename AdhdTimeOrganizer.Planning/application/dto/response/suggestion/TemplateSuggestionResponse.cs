using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner.template;

namespace AdhdTimeOrganizer.Planning.application.dto.response.suggestion;

public record TemplateSuggestionResponse
{
    public required TaskPlannerDayTemplateResponse Template { get; init; }
    // History (applied before): 0=DayOfWeek, 1=DayType. Template metadata, no history: 2=ScheduledDays, 3=SuggestedForDayType.
    // 4=the template of the stretch of time away the day belongs to (F4-02), always first when present.
    public required int PatternType { get; init; }
    // Types 0–3: the invariant DayOfWeek / DayType member name, e.g. "Monday" or "Workday", for the client to translate.
    // Type 4: the stretch's name as the user wrote it ("Barcelona") — shown as-is, never translated.
    public required string PatternLabel { get; init; }
    public required int OccurrenceCount { get; init; } // 0 for pattern types 2, 3 and 4
}
