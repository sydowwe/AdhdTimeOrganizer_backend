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
}
