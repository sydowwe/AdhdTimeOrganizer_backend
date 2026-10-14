using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;

namespace AdhdTimeOrganizer.Planning.domain.model.entity;

/// <summary>
/// F4-02: a named, dated stretch of time away — "Barcelona, 12–20 Aug", "Flu". The user marks it once, across any
/// month boundary, instead of setting the day type on each day.
/// <para>
/// <b>It imposes its kind on its days rather than being read alongside them.</b> <c>TimeAwayStamper</c> writes
/// <see cref="Kind"/> onto every day of the range and links each day back through <c>Calendar.TimeAwayId</c>, so
/// everything that already asks "is this day away?" — the routine excusal, the suggestion ranking, FO6's
/// withdrawal, the nudges — keeps reading <c>Calendar.DayType</c> and never learns stretches exist. The link is
/// what makes the stamp reversible: deleting or shrinking a stretch un-stamps exactly the days it holds, never a
/// day the user marked by hand.
/// </para>
/// <para>
/// Not "period": that word belongs to Routines (<c>RoutineTimePeriod</c>). The UI calls it <i>Time away</i>.
/// </para>
/// </summary>
public class TimeAway : BaseEntityWithUser
{
    /// <summary>What the user calls it. Free to repeat across years ("Skiing").</summary>
    public required string Name { get; set; }

    /// <summary>
    /// <see cref="DayType.Vacation"/> or <see cref="DayType.SickDay"/> — the two away types, and nothing else (the
    /// validator refuses the other three). The enum is reused rather than mirrored so the stamp is a plain
    /// assignment.
    /// </summary>
    public required DayType Kind { get; set; }

    /// <summary>First day, inclusive.</summary>
    public required DateOnly StartDate { get; set; }

    /// <summary>Last day, inclusive.</summary>
    public required DateOnly EndDate { get; set; }

    /// <summary>
    /// The day template the user has in mind for these days. Only <b>suggested</b> — first, as pattern type 4 —
    /// on each day of the stretch; the stretch never applies it (owner decision, 2026-10-12). <c>SET NULL</c>
    /// when the template is deleted.
    /// </summary>
    public long? TemplateId { get; set; }
    public TaskPlannerDayTemplate? Template { get; set; }

    /// <summary>Stamped onto the days whose own location is unset — <c>Travel</c> for a trip.</summary>
    public Location? Location { get; set; }

    public string? Notes { get; set; }
}
