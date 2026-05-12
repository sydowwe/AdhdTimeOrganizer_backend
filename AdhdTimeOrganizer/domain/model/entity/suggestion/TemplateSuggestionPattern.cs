using AdhdTimeOrganizer.domain.model.entity.activityPlanning;

namespace AdhdTimeOrganizer.domain.model.entity.suggestion;

public class TemplateSuggestionPattern
{
    public long UserId { get; set; }
    public long TemplateId { get; set; }
    public TaskPlannerDayTemplate Template { get; set; } = null!;
    public int PatternType { get; set; }   // 0=DayOfWeek, 1=DayType
    public int PatternValue { get; set; }  // 1–7 (DOW) or 0–4 (DayType enum int)
    public int OccurrenceCount { get; set; }
}
