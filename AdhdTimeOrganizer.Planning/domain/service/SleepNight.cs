using AdhdTimeOrganizer.Core.domain.model.entity.user;
using Sydowwe.Framework.Contracts.notification;

namespace AdhdTimeOrganizer.Planning.domain.service;

/// <summary>
/// The night that follows a calendar day: from that day's bed time to the <b>next</b> day's wake-up time, so an
/// early alarm set on one day shortens the night before it and no other. What quiet hours follow when the user
/// switches them onto the sleep schedule (<c>CalendarSleepScheduleReader</c>).
/// <para>
/// A bed time at or before the day's wake-up time is after midnight, as everywhere else
/// (<see cref="UserSleepWindow"/>), so 23:00 is that evening and 01:30 is the small hours of the next date. A
/// night-shift day reads the same way: wake 16:00, bed 08:00 is a night from 08:00 to the next wake-up. A night
/// whose bed time is not before its wake-up — bed time equal to wake-up time, or a bed time after midnight later
/// than the next morning's alarm — is <b>empty</b>: the user said they are not sleeping, so nothing is held.
/// </para>
/// <para>
/// Both boundaries go through <see cref="QuietHoursPolicy.ToInstant"/>, so a night across a DST change is resolved
/// exactly as a fixed quiet-hours window would be: a skipped hour moves to the transition, a repeated one takes
/// the later instant.
/// </para>
/// </summary>
public static class SleepNight
{
    /// <summary>The night after <paramref name="date"/>, as instants; <c>Start &gt;= End</c> means an empty night.</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) After(
        DateOnly date, UserSleepWindow day, TimeOnly nextWakeUpTime, TimeZoneInfo timeZone)
    {
        var bedDate = day.BedTime <= day.WakeUpTime ? date.AddDays(1) : date;

        var start = QuietHoursPolicy.ToInstant(bedDate.ToDateTime(day.BedTime), timeZone);
        var end = QuietHoursPolicy.ToInstant(date.AddDays(1).ToDateTime(nextWakeUpTime), timeZone);
        return (start, end);
    }

    /// <summary>
    /// When the user wakes, if <paramref name="now"/> falls inside a night; null when they are awake. Only the
    /// nights after yesterday and after today can contain the present moment: an earlier night ended at today's
    /// wake-up at the latest, and a later one starts at tomorrow's bed time at the earliest.
    /// </summary>
    /// <param name="dayOf">The sleep window of a user-local date — the calendar day's, or the default for a date with no row.</param>
    public static DateTimeOffset? WakeAt(DateTimeOffset now, TimeZoneInfo timeZone, Func<DateOnly, UserSleepWindow> dayOf)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);

        foreach (var date in new[] { today.AddDays(-1), today })
        {
            var (start, end) = After(date, dayOf(date), dayOf(date.AddDays(1)).WakeUpTime, timeZone);
            if (start <= now && now < end)
                return end;
        }

        return null;
    }
}
