using AdhdTimeOrganizer.application.dto.request.activityHistory.dashboard.calendar;
using AdhdTimeOrganizer.application.dto.response.activityHistory.dashboard.calendar;
using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.dto;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.History.application.endpoint;

/// <summary>
/// The calendar dashboard: one entry per calendar day in the requested range, each carrying the day's
/// own metadata and the history logged on it.
///
/// <para><b>Both sides of the join are the user's days.</b> The days come from
/// <see cref="ICalendarDayLookup"/> keyed by a bare <see cref="DateOnly"/> — those are
/// <c>User.Timezone</c> days, because that is what a person picking a date on a calendar means. So the
/// range is bounded at the user's midnights and each history row is bucketed by the date its
/// <c>StartTimestamp</c> reads on the user's clock, exactly as every sibling dashboard does. Folding in
/// UTC while joining against a user-day calendar is what made the mismatch invisible: a UTC+2 user's
/// 01:00 session landed on the previous day's cell and a UTC−5 user's evening on the next one, in a
/// well-formed 200 with plausible numbers.</para>
/// </summary>
public class CalendarActivityEndpoint(DbContext db, ICalendarDayLookup calendarDayLookup, IUserTimeZoneResolver timeZones)
    : Endpoint<CalendarActivityRequest, List<CalendarActivityDaySummary>>
{
    public override void Configure()
    {
        Post("/activity-history/dashboard/calendar");
    }

    public override async Task HandleAsync(CalendarActivityRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var timeZone = await timeZones.GetAsync(userId, ct);

        // Half-open over the user's midnights: from the midnight opening StartDate to the one opening the
        // day after EndDate, so the whole of EndDate is covered and no instant is counted twice.
        var from = WallClockZone.ToUtc(req.StartDate, TimeOnly.MinValue, timeZone);
        var to = WallClockZone.ToUtc(req.EndDate.AddDays(1), TimeOnly.MinValue, timeZone);

        var calendars = await calendarDayLookup.GetDaysAsync(db, userId, req.StartDate, req.EndDate, ct);

        var histories = await db.Set<ActivityHistory>()
            .AsNoTracking()
            .Include(ah => ah.Activity).ThenInclude(a => a.Role)
            .Where(ah => ah.UserId == userId)
            .Where(ah => ah.StartTimestamp >= from && ah.StartTimestamp < to)
            .ToListAsync(ct);

        var historiesByDate = histories
            // The date the row's start reads on the user's clock — the same key ICalendarDayLookup's
            // days carry, so the dictionary lookup below lines up with the calendar it is joined against.
            .GroupBy(ah => DateOnly.FromDateTime(WallClockZone.FromUtc(ah.StartTimestamp, timeZone)))
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = calendars.Select(c =>
        {
            var dayHistories = historiesByDate.GetValueOrDefault(c.Date, []);

            var totalSeconds = dayHistories.Sum(ah => (long)ah.Length.TotalSeconds);
            var sessionCount = dayHistories.Count;

            // Grouped by role id, not by the ActivityRole instance: two roles are the same group only when
            // they are the same row, and the frontend needs that id to key the day's roles by.
            var topRoles = dayHistories
                .GroupBy(ah => ah.Activity.RoleId)
                .Select(g => new CalendarTopRoleItem
                {
                    RoleId = g.Key,
                    RoleName = g.First().Activity.Role.Name,
                    Color = g.First().Activity.Role.Color,
                    TotalSeconds = g.Sum(ah => (long)ah.Length.TotalSeconds)
                })
                .OrderByDescending(r => r.TotalSeconds)
                .Take(req.TopN)
                .ToList();

            return new CalendarActivityDaySummary
            {
                Id = c.Id,
                Date = c.Date,
                DayType = c.DayType,
                DayIndex = c.DayIndex,
                Label = c.Label,
                HolidayName = c.HolidayName,
                WakeUpTime = new TimeDto(c.WakeUpTime.Hour, c.WakeUpTime.Minute),
                BedTime = new TimeDto(c.BedTime.Hour, c.BedTime.Minute),
                TotalSeconds = totalSeconds,
                SessionCount = sessionCount,
                TopRoles = topRoles
            };
        }).OrderBy(s => s.Date).ToList();

        await Send.ResponseAsync(result, cancellation: ct);
    }
}