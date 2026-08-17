using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.History.application.dto.@enum;
using AdhdTimeOrganizer.History.application.dto.request.activityHistory.dashboard.summary;
using AdhdTimeOrganizer.History.application.dto.response.activityHistory.dashboard;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.History.application.endpoint.activityHistory.activityHistory.query.dashboard.summary;

public class HistorySummaryStackedBarsEndpoint(DbContext db, IUserTimeZoneResolver timeZones)
    : Endpoint<HistorySummaryStackedBarsRequest, HistoryStackedBarsResponse>
{
    public override void Configure()
    {
        Post("/activity-history/dashboard/summary/stacked-bars");
    }

    public override async Task HandleAsync(HistorySummaryStackedBarsRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        // The user's own days and the user's own clock. WindowStartTime / WindowEndTime are bare TimeDto,
        // which in this solution means zone-less wall clock — the same convention the detail dashboard and
        // every command path use. They used to be added as raw offsets to a UTC midnight, i.e. read as UTC,
        // which put this endpoint's bars in a different hour range than the detail endpoint's for the same
        // picked window.
        var timeZone = await timeZones.GetAsync(userId, ct);
        var (fromDate, toDate) = req.ToDateRange();
        var (from, to) = req.ToUtcRange(timeZone);

        var records = await db.Set<ActivityHistory>()
            .Include(ah => ah.Activity).ThenInclude(a => a.Role)
            .Include(ah => ah.Activity).ThenInclude(a => a.Category)
            .Where(ah => ah.UserId == userId)
            .Where(ah => ah.StartTimestamp >= from && ah.StartTimestamp < to)
            .ToListAsync(ct);

        var windowMinutes = req.WindowMinutes;

        List<(DateTime Start, DateTime End)> windows;

        if (windowMinutes < 1440)
            windows = GenerateFixedWindows(
                fromDate, toDate, windowMinutes,
                req.WindowStartTime.ToTimeOnly(), req.WindowEndTime.ToTimeOnly(), timeZone);
        else
            windows = GenerateWindows(fromDate, toDate, windowMinutes / 1440, timeZone);

        var response = new HistoryStackedBarsResponse
        {
            Windows = windows.Select(w => new HistoryWindow
            {
                WindowStart = w.Start,
                WindowEnd = w.End,
                Items = records
                    .Where(ah => ah.StartTimestamp >= w.Start && ah.StartTimestamp < w.End)
                    .GroupBy(ah => ResolveGroupKey(ah, req.GroupBy))
                    .Select(g => new HistoryGroupItem
                    {
                        Name = g.Key.Name,
                        TotalSeconds = g.Sum(ah => ah.Length.TotalSeconds),
                        Color = g.Key.Color
                    })
                    .OrderByDescending(i => i.TotalSeconds)
                    .ToList()
            }).ToList()
        };

        await Send.ResponseAsync(response, cancellation: ct);
    }

    /// <summary>
    /// One sub-day band per day, between the user's chosen wall-clock hours. The outer walk is over
    /// <b>calendar dates</b> and each band boundary is resolved through the zone, rather than the previous
    /// walk over UTC ticks: on a DST transition day the user's 08:00 is a different number of hours after
    /// the previous 08:00, and adding 24h to an instant silently slides every bar by the offset change.
    /// </summary>
    private static List<(DateTime Start, DateTime End)> GenerateFixedWindows(
        DateOnly fromDate, DateOnly toDate, int windowMinutes,
        TimeOnly startTime, TimeOnly endTime, TimeZoneInfo timeZone)
    {
        var windows = new List<(DateTime Start, DateTime End)>();

        for (var day = fromDate; day <= toDate; day = day.AddDays(1))
        {
            var dayStart = WallClockZone.ToUtc(day, startTime, timeZone);
            // endTime <= startTime is a band running past midnight into the next date, matching the
            // over-midnight rule DateAndTimeRangeDto.ToDateTimeRange applies to the detail dashboard.
            var dayEnd = endTime <= startTime
                ? WallClockZone.ToUtc(day.AddDays(1), endTime, timeZone)
                : WallClockZone.ToUtc(day, endTime, timeZone);

            var current = dayStart;
            while (current < dayEnd)
            {
                var next = current.AddMinutes(windowMinutes);
                if (next > dayEnd)
                    next = dayEnd;
                windows.Add((current, next));
                current = current.AddMinutes(windowMinutes);
            }
        }

        return windows;
    }

    /// <summary>
    /// Whole-day bands. Boundaries are the user's midnights, so a band is 23 or 25 hours long across a DST
    /// transition — which is correct: the bar covers the days it says it covers.
    /// </summary>
    private static List<(DateTime Start, DateTime End)> GenerateWindows(
        DateOnly fromDate, DateOnly toDate, int windowDays, TimeZoneInfo timeZone)
    {
        var windows = new List<(DateTime Start, DateTime End)>();
        var endExclusive = toDate.AddDays(1);

        for (var day = fromDate; day < endExclusive; day = day.AddDays(windowDays))
        {
            var next = day.AddDays(windowDays);
            if (next > endExclusive)
                next = endExclusive;

            windows.Add((
                WallClockZone.ToUtc(day, TimeOnly.MinValue, timeZone),
                WallClockZone.ToUtc(next, TimeOnly.MinValue, timeZone)));
        }

        return windows;
    }

    private static (string Name, string? Color) ResolveGroupKey(ActivityHistory ah, HistoryGroupBy groupBy)
    {
        return groupBy switch
        {
            HistoryGroupBy.Activity => (ah.Activity.Name, null),
            HistoryGroupBy.Role => (ah.Activity.Role.Name, ah.Activity.Role.Color),
            HistoryGroupBy.Category => ah.Activity.Category != null
                ? (ah.Activity.Category.Name, ah.Activity.Category.Color)
                : ("Uncategorized", null),
            _ => (ah.Activity.Name, null)
        };
    }
}