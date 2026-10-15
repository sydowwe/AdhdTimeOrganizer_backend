namespace AdhdTimeOrganizer.Core.domain.model.@enum;

/// <summary>
/// The behaviour a <see cref="DayType"/> carries beyond its name. Lives beside the enum, in Core, because both
/// readers — Planning's suggestion ranking and Routines' streak excusal — must agree on it and cannot see each
/// other.
/// </summary>
public static class DayTypeExtensions
{
    /// <summary>
    /// The day types that mean the user is <b>away</b>: on holiday or ill. <see cref="DayType.Special"/> is not
    /// one of them — it means "unusual", not "absent" (owner decision, 2026-10-06). A plain array so a query can
    /// say <c>DayTypeExtensions.AwayTypes.Contains(c.DayType)</c> and translate, which <see cref="IsAway"/>
    /// cannot.
    /// </summary>
    public static readonly DayType[] AwayTypes = [DayType.Vacation, DayType.SickDay];

    /// <summary>
    /// Whether a day of this type is an away day. An away day excuses routine cycles, silences routine nudges,
    /// and makes the planner suggest by day type rather than by weekday.
    /// </summary>
    public static bool IsAway(this DayType dayType) => dayType is DayType.Vacation or DayType.SickDay;

    /// <summary>
    /// The day types the planner matches a day by, strongest first: template suggestions, and recurrence rules of
    /// type <c>DayType</c> (both the suggestion panel and the auto-placing materialiser). Usually just the stored
    /// type. A public holiday on a weekday is stored as <see cref="DayType.Workday"/> with a holiday key, and answers
    /// to <see cref="DayType.Weekend"/> first and <see cref="DayType.Workday"/> second (F4 question 4, owner decision
    /// 2026-10-15): most people do not work on one, so a weekend template ranks first and a weekend rule is offered,
    /// while a workday rule still fires. A holiday on a Saturday or Sunday is already a weekend and changes nothing.
    /// <para>
    /// <b>This is matching only, never time away.</b> A holiday is not <see cref="IsAway"/>: Routines excuses no cycle
    /// for one, the nudges still run, and the stored type is never rewritten. The method takes the key rather than the
    /// calendar row because the row is Planning's, and Core cannot see it.
    /// </para>
    /// </summary>
    public static DayType[] PlanningDayTypes(this DayType storedType, string? holidayKey) =>
        storedType == DayType.Workday && holidayKey is not null ? [DayType.Weekend, DayType.Workday] : [storedType];
}
