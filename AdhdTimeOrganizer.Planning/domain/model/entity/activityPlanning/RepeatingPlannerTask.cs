using AdhdTimeOrganizer.Planning.domain.model.@enum;

namespace AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;

public class RepeatingPlannerTask : BasePlannerTask
{
    public bool IsActive { get; set; } = true;
    public required RecurrenceType RecurrenceType { get; set; }
    public ICollection<string> ScheduledDays { get; set; } = [];
    public ICollection<int> ScheduledDates { get; set; } = [];
    public DateOnly? ActiveFromDate { get; set; }
    public DateOnly? ActiveToDate { get; set; }
    public ICollection<string> ScheduledForDayTypes { get; set; } = [];

    /// <summary>
    /// Opt-in (FO6): the rule puts its own occurrences on today's and tomorrow's plan, linked back through
    /// <see cref="PlannerTask.SourceRepeatingTaskId"/>. Added with FO3 phase 2's migration; nothing reads it yet.
    /// </summary>
    public bool AutoPlace { get; set; }

    public string Color => Activity.Role.Color;
}