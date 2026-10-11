using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity;

namespace AdhdTimeOrganizer.Planning.domain.service;

/// <summary>
/// Builds the <see cref="Calendar"/> row for one (user, date). The single definition of what a day looks like
/// before anyone has planned it — its day type, its holiday name and the sleep window it starts with.
/// <para>
/// Both paths that can bring a calendar row into existence go through here: <c>CalendarSeeder</c>, which fills
/// whole years at user setup, and <c>CalendarProvisioner</c>, which creates one on demand when a task lands on
/// a date that has none. They used to be one path, and the defaults lived inline in the seeder loop; the
/// moment a second creator existed, that made "planned in advance" and "planned on the spot" two different
/// kinds of day.
/// </para>
/// </summary>
public static class CalendarDayFactory
{
    /// <summary>
    /// The day for <paramref name="userId"/> on <paramref name="date"/>, starting with the user's own sleep window
    /// (FO10, <see cref="UserSleepWindow.ForUserAsync"/>). The day does not carry
    /// <see cref="Calendar.HasCustomSleepWindow"/>, so it keeps following the default until the user edits it.
    /// </summary>
    public static Calendar Create(long userId, DateOnly date, UserSleepWindow sleepWindow)
    {
        var holiday = HolidayCalendar.ForDate(date);

        return new Calendar
        {
            UserId = userId,
            Date = date,
            DayType = DayTypeFor(date),
            // Holidays do not override the day type — a holiday on a Tuesday is still a Workday row with a
            // HolidayName, which is what the seeder has always produced and what the day view renders.
            // Key and name are written together, from the same table entry, so they cannot disagree.
            HolidayKey = holiday?.Key,
            HolidayName = holiday?.Name,
            WakeUpTime = sleepWindow.WakeUpTime,
            BedTime = sleepWindow.BedTime
        };
    }

    private static DayType DayTypeFor(DateOnly date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? DayType.Weekend : DayType.Workday;
}
