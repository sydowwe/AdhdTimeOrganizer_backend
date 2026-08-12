using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;

namespace AdhdTimeOrganizer.Planning.domain.model.entity.suggestion;

public class PlannerSuggestionFromDayTemplate : BaseEntityWithUser
{
    public long TemplateId { get; set; }
    public TaskPlannerDayTemplate Template { get; set; } = null!;
    public int PatternType { get; set; } // 0=DayOfWeek, 1=DayType
    public int PatternValue { get; set; } // 1–7 (DOW) or 0–4 (DayType enum int)
    public int OccurrenceCount { get; set; }
}