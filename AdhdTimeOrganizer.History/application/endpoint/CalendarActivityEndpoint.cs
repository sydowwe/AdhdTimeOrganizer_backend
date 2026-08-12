using AdhdTimeOrganizer.application.dto.request.activityHistory.dashboard.calendar;
using AdhdTimeOrganizer.application.dto.response.activityHistory.dashboard.calendar;
using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.dto;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.History.application.endpoint;

public class CalendarActivityEndpoint(DbContext db, ICalendarDayLookup calendarDayLookup) : Endpoint<CalendarActivityRequest, List<CalendarActivityDaySummary>>
{
    public override void Configure()
    {
        Post("/activity-history/dashboard/calendar");
    }

    public override async Task HandleAsync(CalendarActivityRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var from = req.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = req.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var calendars = await calendarDayLookup.GetDaysAsync(db, userId, req.StartDate, req.EndDate, ct);

        var histories = await db.Set<ActivityHistory>()
            .Include(ah => ah.Activity).ThenInclude(a => a.Role)
            .Where(ah => ah.UserId == userId)
            .Where(ah => ah.StartTimestamp >= from && ah.StartTimestamp < to)
            .ToListAsync(ct);

        var historiesByDate = histories
            .GroupBy(ah => DateOnly.FromDateTime(ah.StartTimestamp))
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = calendars.Select(c =>
        {
            var dayHistories = historiesByDate.GetValueOrDefault(c.Date, []);

            var totalSeconds = dayHistories.Sum(ah => (long)ah.Length.TotalSeconds);
            var sessionCount = dayHistories.Count;

            var topRoles = dayHistories
                .GroupBy(ah => ah.Activity.Role)
                .Select(g => new CalendarTopRoleItem
                {
                    RoleName = g.Key.Name,
                    Color = g.Key.Color ?? "",
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