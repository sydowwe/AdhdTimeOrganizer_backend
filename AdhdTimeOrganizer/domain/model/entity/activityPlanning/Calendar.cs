using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.domain.model.entity.activityPlanning;

public class Calendar : BaseEntityWithUser
{
    public required DateOnly Date { get; init; }

    // Day metadata
    public required DayType DayType { get; set; } // Workday, Weekend, Holiday, Vacation, SickDay, Special
    public string? HolidayName { get; init; } // Name of the holiday (e.g., "Christmas", "New Year")
    public string? Label { get; set; } // "HomeOffice", "Office", custom labels - independent of holiday

    // Sleep schedule for this day
    public required TimeOnly WakeUpTime { get; set; }
    public required TimeOnly BedTime { get; set; }

    // Planning status
    public long? AppliedTemplateId { get; set; }
    public string? AppliedTemplateName { get; set; }

    // Day-specific notes
    public string? Weather { get; set; }
    public string? Notes { get; set; }

    // Collections - this groups everything for the day
    public virtual ICollection<PlannerTask> Tasks { get; set; } = new List<PlannerTask>();
    // Future: Events, TodoLists, Habits, etc.

    public int DayIndex => Date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)Date.DayOfWeek;
    public bool IsWeekend => DayIndex is 6 or 7;
    // Completion tracking
    public int TotalTasks => Tasks.Count;
    public int CompletedTasks => Tasks.Select(t => t.IsDone).Count();
    public int CompletionRate => TotalTasks > 0 ? (CompletedTasks * 100) / TotalTasks : 0;
}