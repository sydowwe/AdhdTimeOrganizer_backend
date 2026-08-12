using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Planning.application.seam;

/// <summary>
/// Reads <see cref="Calendar"/> rows through Core's seam, so History's calendar dashboard can join
/// against day metadata without referencing Planning.
/// </summary>
public sealed class CalendarDayLookup : ICalendarDayLookup, IScopedService
{
    public async Task<List<CalendarDaySummary>> GetDaysAsync(DbContext db, long userId, DateOnly startDate, DateOnly endDate, CancellationToken ct)
    {
        var calendars = await db.Set<Calendar>()
            .Where(c => c.UserId == userId)
            .Where(c => c.Date >= startDate && c.Date <= endDate)
            .ToListAsync(ct);

        // DayIndex is a computed property (Date.DayOfWeek-derived), not a mapped column, so it is
        // projected in memory rather than in the query above.
        return calendars
            .Select(c => new CalendarDaySummary(c.Id, c.Date, c.DayType, c.DayIndex, c.Label, c.HolidayName, c.WakeUpTime, c.BedTime))
            .ToList();
    }
}
