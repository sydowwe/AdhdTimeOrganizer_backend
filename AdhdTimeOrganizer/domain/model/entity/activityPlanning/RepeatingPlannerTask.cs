using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.domain.model.entity.activityPlanning;

public class RepeatingPlannerTask : BasePlannerTask
{
    public bool IsActive { get; set; } = true;
    public required RecurrenceType RecurrenceType { get; set; }
    public ICollection<string> ScheduledDays { get; set; } = [];
    public ICollection<int> ScheduledDates { get; set; } = [];
    public DateOnly? ActiveFromDate { get; set; }
    public DateOnly? ActiveToDate { get; set; }
    public ICollection<string> ScheduledForDayTypes { get; set; } = [];

    public string Color => Activity.Role.Color;
}
