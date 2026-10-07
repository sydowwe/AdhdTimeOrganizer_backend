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
    /// <see cref="PlannerTask.SourceRepeatingTaskId"/>. Read by <c>RepeatingTaskMaterialiser</c>.
    /// </summary>
    public bool AutoPlace { get; set; }

    /// <summary>
    /// The last date the materialiser has <b>decided</b> for this rule (FO6): placed an occurrence, found one
    /// already there, or passed the day over (no match, an away day, dropped by the conflict mode). Null until
    /// the first decision. The materialiser only looks at dates after it, so a rule is materialised forward
    /// and never twice.
    /// <para>
    /// This is what keeps a deleted occurrence deleted. The "already placed" evidence on <c>planner_task</c> goes
    /// with the row, so a dedupe on that table alone would put a deleted block straight back on the next read.
    /// The watermark lives on the rule, which deleting an occurrence never touches.
    /// </para>
    /// <para>Not on any DTO: it is bookkeeping, not something the user sets.</para>
    /// </summary>
    public DateOnly? MaterialisedThrough { get; set; }

    public string Color => Activity.Role.Color;
}