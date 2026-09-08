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
        // No tracking and seven columns rather than whole entities: this is a read for someone else's
        // dashboard, so full-row I/O and a change-tracker entry per day are both paid for nothing.
        var days = await db.Set<Calendar>()
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .Where(c => c.Date >= startDate && c.Date <= endDate)
            .Select(c => new { c.Id, c.Date, c.DayType, c.Label, c.HolidayName, c.WakeUpTime, c.BedTime })
            .ToListAsync(ct);

        // DayIndex is a computed property (Date.DayOfWeek-derived), not a mapped column, so it is
        // projected in memory rather than in the query above — it is the eighth field and the only one
        // that cannot be selected.
        return days
            .Select(c => new CalendarDaySummary(
                c.Id, c.Date, c.DayType, Calendar.DayIndexOf(c.Date),
                c.Label, c.HolidayName, c.WakeUpTime, c.BedTime))
            .ToList();
    }
}
