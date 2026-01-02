using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.domain.model.entity.activityPlanning;

public class Calendar : BaseEntityWithUser
{
    public required DateOnly Date { get; set; }

    // Day metadata
    public DayType DayType { get; set; } // Workday, Weekend, Holiday, Vacation
    public string? Label { get; set; } // "HomeOffice", "Office", "Sick Day", custom labels

    // Sleep schedule for this day
    public TimeOnly? WakeUpTime { get; set; }
    public TimeOnly? BedTime { get; set; }

    // Planning status
    public long? AppliedTemplateId { get; set; }
    public string? AppliedTemplateName { get; set; }

    // Day-specific notes
    public string? Weather { get; set; }
    public string? Notes { get; set; }

    // Collections - this groups everything for the day
    public virtual ICollection<PlannerTask> Tasks { get; set; } = new List<PlannerTask>();
    // Future: Events, TodoLists, Habits, etc.

    public int DayIndex => Date.DayOfWeek == DayOfWeek.Sunday ? 7 :(int)Date.DayOfWeek;
    // Completion tracking
    public int TotalTasks => Tasks.Count;
    public int CompletedTasks => Tasks.Select(t => t.IsDone).Count();
    public int CompletionRate => TotalTasks > 0 ? (CompletedTasks * 100) / TotalTasks : 0;
}