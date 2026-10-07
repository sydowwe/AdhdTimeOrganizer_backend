using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;

namespace AdhdTimeOrganizer.Planning.domain.service;

/// <summary>
/// Whether a repeating rule falls on a day. One implementation behind two callers on purpose: the day planner's
/// suggestion panel (<c>GetSuggestionsRepeatingPlannerTaskEndpoint</c>) and the materialiser that puts an
/// auto-placing rule's occurrences on the plan (<c>RepeatingTaskMaterialiser</c>, FO6). A second copy is how the
/// panel would start offering a rule on a day the plan never receives it, or the other way round.
/// <para>
/// <see cref="IsActive"/> is deliberately not part of the answer — the suggestion read filters it in SQL, and
/// the materialiser's callers ask about an inactive rule only to learn what it used to place.
/// </para>
/// </summary>
public static class RepeatingPlannerTaskSchedule
{
    public static bool FallsOn(RepeatingPlannerTask rule, DateOnly date, DayType dayType) =>
        IsWithinActiveWindow(rule, date) && rule.RecurrenceType switch
        {
            RecurrenceType.DayOfWeek => rule.ScheduledDays.Contains(date.DayOfWeek.ToString()),
            RecurrenceType.DayOfMonth => rule.ScheduledDates.Contains(date.Day),
            RecurrenceType.DayType => rule.ScheduledForDayTypes.Contains(dayType.ToString()),
            // The window is the whole rule, and IsWithinActiveWindow has already required both of its bounds.
            RecurrenceType.DateRange => true,
            _ => false
        };

    /// <summary>
    /// The active window bounds every recurrence type, not only <c>DateRange</c>: a weekly or monthly rule may
    /// carry one ("every Tue until 30 Nov"). An unset bound is open. A <c>DateRange</c> rule needs both bounds,
    /// because the window is its whole rule. The suggestion endpoint translates the same predicate to SQL as a
    /// pre-filter; keep the two in step.
    /// </summary>
    public static bool IsWithinActiveWindow(RepeatingPlannerTask rule, DateOnly date) =>
        (rule.ActiveFromDate == null || rule.ActiveFromDate <= date)
        && (rule.ActiveToDate == null || date <= rule.ActiveToDate)
        && (rule.RecurrenceType != RecurrenceType.DateRange || (rule.ActiveFromDate != null && rule.ActiveToDate != null));
}
