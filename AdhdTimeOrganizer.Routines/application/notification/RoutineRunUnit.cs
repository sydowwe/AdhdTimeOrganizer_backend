namespace AdhdTimeOrganizer.Routines.application.notification;

/// <summary>
/// The unit a routine run is counted in, for <see cref="RoutinePeriodEndedNotificationKind"/>'s wording. A streak counts periods, but "7 periods
/// in a row" reads badly when the period is a day or a week.
/// <para>
/// The same rule as the client's <c>useRoutineRunLabel.ts</c> (<c>unitSuffix</c>), so the bell and the
/// snackbar name a run the same way. Change both or neither. A length of 0 comes from a row persisted before
/// the payload carried the length, and reads as plain periods.
/// </para>
/// </summary>
public enum RoutineRunUnit
{
    Day,
    Week,
    Month,
    Period
}

public static class RoutineRunUnits
{
    public static RoutineRunUnit For(int periodLengthInDays) => periodLengthInDays switch
    {
        1 => RoutineRunUnit.Day,
        7 => RoutineRunUnit.Week,
        >= 28 and <= 31 => RoutineRunUnit.Month,
        _ => RoutineRunUnit.Period
    };
}
