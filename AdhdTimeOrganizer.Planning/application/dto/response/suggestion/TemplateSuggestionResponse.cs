using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner.template;

namespace AdhdTimeOrganizer.Planning.application.dto.response.suggestion;

public record TemplateSuggestionResponse
{
    public required TaskPlannerDayTemplateResponse Template { get; init; }
    // History (applied before): 0=DayOfWeek, 1=DayType. Template metadata, no history: 2=ScheduledDays, 3=SuggestedForDayType.
    // 4=the template of the stretch of time away the day belongs to (F4-02), always first when present.
    // 5=SuggestedLocation equals the day's location (C3): template metadata, ranked first within it.
    // 6=a weekend match on a public holiday that falls on a weekday (F4 Q4): applied on weekends before (count > 0) or
    // suggested for weekends (count 0). Ranks right after type 4; the day is still stored as a Workday.
    public required int PatternType { get; init; }
    // Types 0–3: the invariant DayOfWeek / DayType member name, e.g. "Monday" or "Workday", for the client to translate.
    // Type 4: the stretch's name as the user wrote it ("Barcelona") — shown as-is, never translated.
    // Type 5: the invariant Location member name, e.g. "Office", for the client to translate.
    // Type 6: the day's HolidayKey, e.g. "EASTER_MONDAY", for the client to translate (calendar.holidays.<key>).
    public required string PatternLabel { get; init; }
    public required int OccurrenceCount { get; init; } // 0 for pattern types 2, 3, 4 and 5; for 6, the weekend history's count or 0
}
