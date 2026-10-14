namespace AdhdTimeOrganizer.Routines.domain.service;

/// <summary>
/// Which period streaks are worth celebrating: the end-of-period summary (<c>RoutinePeriodEndedPayload</c>) and
/// the client's confetti both read this, the client through <c>RoutineTimePeriodResponse.NextStreakMilestone</c>
/// and <c>RecordToBeat</c>, so the bell and the snackbar can't disagree.
/// <para>
/// <b>Milestones are measured in time felt, not in periods counted.</b> A streak counts periods, so "30" means a
/// month for a daily routine and more than half a year for a weekly one. The milestones are about a week, a
/// month, a quarter, half a year and then every whole year, each converted to this period's count. Daily gets
/// 7 / 30 / 90 / 180 / 365, weekly 4 / 13 / 26 / 52, monthly 3 / 6 / 12. The first one comes early on purpose:
/// most habits are dropped in the first weeks, and a reward that comes too late doesn't help someone who
/// discounts delayed rewards steeply. After that the gaps widen, because a celebration that comes often stops
/// meaning anything.
/// </para>
/// </summary>
public static class RoutineStreakMilestones
{
    /// <summary>
    /// The shortest previous record whose passing counts as a new best. Beating a run of 1 or 2 is what any
    /// new routine does in its first days, and announcing that would teach the user to ignore the sentence.
    /// </summary>
    public const int MinimumRecordToBeat = 3;

    /// <summary>A week, a month, a quarter, half a year. Whole years are added on top of these, without end.</summary>
    private static readonly int[] FeltDays = [7, 30, 90, 180];

    private const int DaysPerYear = 365;

    /// <summary>
    /// A milestone of 1 would mean "you did it once" for any period of a week or longer, which isn't a run yet.
    /// </summary>
    private const int MinimumMilestone = 2;

    /// <summary>Whether <paramref name="streak"/> is itself a milestone for a period of this length.</summary>
    public static bool IsMilestone(int streak, int lengthInDays) =>
        streak >= MinimumMilestone && Next(streak - 1, lengthInDays) == streak;

    /// <summary>
    /// The first milestone above <paramref name="streak"/>. There is always one, since every whole year adds a
    /// milestone.
    /// </summary>
    public static int Next(int streak, int lengthInDays)
    {
        var length = Math.Max(lengthInDays, 1);
        var next = int.MaxValue;

        foreach (var days in FeltDays)
            Consider(PeriodsIn(days, length));

        // Start at the year the streak has already reached. Rounding can put that year's count at or below the
        // streak, so walk forward until one is above it.
        for (var year = Math.Max(1L, (long)streak * length / DaysPerYear); ; year++)
        {
            var count = PeriodsIn(year * DaysPerYear, length);
            if (count > streak && count >= MinimumMilestone)
            {
                Consider(count);
                break;
            }
        }

        return next;

        void Consider(int count)
        {
            if (count > streak && count >= MinimumMilestone && count < next)
                next = count;
        }
    }

    private static int PeriodsIn(long days, int length) =>
        (int)Math.Round((double)days / length, MidpointRounding.AwayFromZero);
}
