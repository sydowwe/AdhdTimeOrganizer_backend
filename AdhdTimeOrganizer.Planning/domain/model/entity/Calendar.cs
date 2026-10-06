using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;

namespace AdhdTimeOrganizer.Planning.domain.model.entity;

public class Calendar : BaseEntityWithUser
{
    public required DateOnly Date { get; init; }

    // Day metadata
    // Workday, Weekend, Vacation, SickDay, Special — the five DayType members, and there is deliberately no
    // Holiday among them: a public holiday is a Workday row carrying a HolidayName (see CalendarDayFactory),
    // so that "is it a holiday" and "what kind of day is it" stay separate questions.
    public required DayType DayType { get; set; }

    /// <summary>
    /// Stable key of the public holiday on this date (<c>CHRISTMAS_DAY</c>), or null. What a client translates
    /// through its locale table; never shown as-is. Written once by <c>CalendarDayFactory</c> together with
    /// <see cref="HolidayName"/>, both off the same <c>HolidayCalendar</c> entry.
    /// </summary>
    public string? HolidayKey { get; init; }

    /// <summary>The holiday's native Slovak name — the fallback for a reader that has no locale table.</summary>
    public string? HolidayName { get; init; }
    /// <summary>
    /// A free-text marker for the day ("Flight 07:40", "Mum's birthday"), independent of the holiday. Not where the
    /// user works — that is <see cref="Location"/> — and not what kind of day it is, which is <see cref="DayType"/>.
    /// Nothing on the server reads it.
    /// </summary>
    public string? Label { get; set; }

    // Sleep schedule for this day
    public required TimeOnly WakeUpTime { get; set; }
    public required TimeOnly BedTime { get; set; }

    // Planning status
    public long? AppliedTemplateId { get; set; }
    public string? AppliedTemplateName { get; set; }

    // Location
    public Location? Location { get; set; }

    // Day-specific notes
    public string? Weather { get; set; }
    public string? Notes { get; set; }

    // Collections - this groups everything for the day
    public virtual ICollection<PlannerTask> Tasks { get; set; } = [];
    // Future: Events, TodoLists, Habits, etc.

    public int DayIndex => DayIndexOf(Date);

    /// <summary>
    /// ISO day index (Monday = 1 … Sunday = 7) for a date. Exposed as a static so a caller that projected the
    /// date out of the row — <c>CalendarDayLookup</c> selects columns rather than entities — computes the same
    /// index rather than writing the Sunday special case a second time.
    /// </summary>
    public static int DayIndexOf(DateOnly date) => date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;

    public bool IsWeekend => DayIndex is 6 or 7;

    // Completion tracking
    public int TotalTasks => Tasks.Count;
    public int CompletedTasks => Tasks.Count(t => t.IsDone);
}