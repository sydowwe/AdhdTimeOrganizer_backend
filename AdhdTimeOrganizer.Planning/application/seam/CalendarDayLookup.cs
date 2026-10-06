using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Planning.application.seam;

/// <summary>
/// Reads <see cref="Calendar"/> rows through Core's seam, so History's calendar dashboard and Routines' streak
/// and nudge rules can read day metadata without referencing Planning.
/// </summary>
public sealed class CalendarDayLookup : ICalendarDayLookup, IScopedService
{
    public async Task<List<CalendarDaySummary>> GetDaysAsync(DbContext db, long userId, DateOnly startDate, DateOnly endDate, CancellationToken ct)
    {
        // No tracking and eight columns rather than whole entities: this is a read for someone else's
        // dashboard, so full-row I/O and a change-tracker entry per day are both paid for nothing.
        var days = await db.Set<Calendar>()
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .Where(c => c.Date >= startDate && c.Date <= endDate)
            .Select(c => new { c.Id, c.Date, c.DayType, c.Label, c.HolidayKey, c.HolidayName, c.WakeUpTime, c.BedTime })
            .ToListAsync(ct);

        // DayIndex is a computed property (Date.DayOfWeek-derived), not a mapped column, so it is
        // projected in memory rather than in the query above — it is the ninth field and the only one
        // that cannot be selected.
        return days
            .Select(c => new CalendarDaySummary(
                c.Id, c.Date, c.DayType, Calendar.DayIndexOf(c.Date),
                c.Label, c.HolidayKey, c.HolidayName, c.WakeUpTime, c.BedTime))
            .ToList();
    }

    public async Task<IReadOnlyDictionary<long, IReadOnlySet<DateOnly>>> GetAwayDatesAsync(
        DbContext db, IReadOnlyCollection<long> userIds, DateOnly startDate, DateOnly endDate, CancellationToken ct)
    {
        if (userIds.Count == 0 || endDate < startDate)
            return new Dictionary<long, IReadOnlySet<DateOnly>>();

        // Explicit user predicate, never the ambient filter: the sweeps calling this run unauthenticated, where
        // the filter is inert, and a request caller passes its own id anyway.
        var rows = await db.Set<Calendar>()
            .AsNoTracking()
            .Where(c => userIds.Contains(c.UserId))
            .Where(c => c.Date >= startDate && c.Date <= endDate)
            .Where(c => DayTypeExtensions.AwayTypes.Contains(c.DayType))
            .Select(c => new { c.UserId, c.Date })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, IReadOnlySet<DateOnly> (g) => g.Select(r => r.Date).ToHashSet());
    }
}
