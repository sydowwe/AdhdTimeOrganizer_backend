using AdhdTimeOrganizer.application.dto.@enum;
using AdhdTimeOrganizer.application.dto.request.activityHistory.dashboard.summary;
using AdhdTimeOrganizer.application.dto.response.activityHistory.dashboard;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.activityHistory.query.dashboard.summary;

public class HistorySummaryStackedBarsEndpoint(AppDbContext db) : Endpoint<HistorySummaryStackedBarsRequest, HistoryStackedBarsResponse>
{
    public override void Configure()
    {
        Post("/activity-history/dashboard/summary/stacked-bars");
    }

    public override async Task HandleAsync(HistorySummaryStackedBarsRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var (fromDate, toDate) = req.ToDateRange();
        var from = fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = toDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var records = await db.ActivityHistories
            .Include(ah => ah.Activity).ThenInclude(a => a.Role)
            .Include(ah => ah.Activity).ThenInclude(a => a.Category)
            .Where(ah => ah.UserId == userId)
            .Where(ah => ah.StartTimestamp >= from && ah.StartTimestamp < to)
            .ToListAsync(ct);

        var windowMinutes = req.WindowMinutes;

        List<(DateTime Start, DateTime End)> windows;

        if (windowMinutes < 1440)
        {
            var startOffset = TimeSpan.FromHours(req.WindowStartTime.Hours).Add(TimeSpan.FromMinutes(req.WindowStartTime.Minutes));
            var endOffset = TimeSpan.FromHours(req.WindowEndTime.Hours).Add(TimeSpan.FromMinutes(req.WindowEndTime.Minutes));
            windows = GenerateFixedWindows(from, to, windowMinutes, startOffset, endOffset);
        }
        else
        {
            windows = GenerateWindows(from, to, windowMinutes / 1440);
        }

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

        await SendAsync(response, cancellation: ct);
    }

    private static List<(DateTime Start, DateTime End)> GenerateFixedWindows(
        DateTime from, DateTime to, int windowMinutes, TimeSpan startOffset, TimeSpan endOffset)
    {
        var windows = new List<(DateTime Start, DateTime End)>();
        var day = from.Date;

        while (day < to)
        {
            var dayStart = day.Add(startOffset);
            var dayEnd = day.Add(endOffset);
            if (dayEnd <= dayStart) dayEnd = dayEnd.AddDays(1);

            var current = dayStart;
            while (current < dayEnd)
            {
                var next = current.AddMinutes(windowMinutes);
                if (next > dayEnd) next = dayEnd;
                windows.Add((current, next));
                current = current.AddMinutes(windowMinutes);
            }

            day = day.AddDays(1);
        }

        return windows;
    }

    private static List<(DateTime Start, DateTime End)> GenerateWindows(DateTime from, DateTime to, int windowDays)
    {
        var windows = new List<(DateTime Start, DateTime End)>();
        var current = from;
        while (current < to)
        {
            var next = current.AddDays(windowDays);
            if (next > to) next = to;
            windows.Add((current, next));
            current = current.AddDays(windowDays);
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
