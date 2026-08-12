using AdhdTimeOrganizer.Core.domain.model.@enum;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.seam;

/// <summary>
/// Reads calendar-day rows for a date range without the caller naming <c>Calendar</c>, which belongs
/// to Planning. History's calendar dashboard needs a day's <see cref="DayType"/> / sleep-schedule
/// metadata to join against <c>ActivityHistory</c>; reading it directly would force a
/// History → Planning project reference. Planning implements this; History depends on Core alone.
/// </summary>
/// <remarks>
/// Resolved as a <em>single</em> service, not a keyed <c>IEnumerable</c> like
/// <see cref="IActivityMembershipSource"/> — same reasoning as <see cref="IActivityTimeAttributionSink"/>:
/// a missing registration throws at endpoint activation instead of silently returning an empty calendar.
/// </remarks>
public interface ICalendarDayLookup : ISeam
{
    Task<List<CalendarDaySummary>> GetDaysAsync(DbContext db, long userId, DateOnly startDate, DateOnly endDate, CancellationToken ct);
}

public sealed record CalendarDaySummary(
    long Id,
    DateOnly Date,
    DayType DayType,
    int DayIndex,
    string? Label,
    string? HolidayName,
    TimeOnly WakeUpTime,
    TimeOnly BedTime);
