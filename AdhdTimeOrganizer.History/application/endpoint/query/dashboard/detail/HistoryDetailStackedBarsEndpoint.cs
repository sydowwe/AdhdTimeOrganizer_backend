using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.History.application.dashboard;
using AdhdTimeOrganizer.History.application.dto.request.activityHistory.dashboard.detail;
using AdhdTimeOrganizer.History.application.dto.response.activityHistory.dashboard;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.History.application.endpoint.activityHistory.activityHistory.query.dashboard.detail;

public class HistoryDetailStackedBarsEndpoint(DbContext db, IUserTimeZoneResolver timeZones)
    : Endpoint<HistoryDetailStackedBarsRequest, HistoryStackedBarsResponse>
{
    public override void Configure()
    {
        Post("/activity-history/dashboard/detail/stacked-bars");
    }

    public override async Task HandleAsync(HistoryDetailStackedBarsRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var (from, to) = req.ToDateTimeRange(await timeZones.GetAsync(userId, ct));

        var records = await db.Set<ActivityHistory>()
            .Include(ah => ah.Activity).ThenInclude(a => a.Role)
            .Include(ah => ah.Activity).ThenInclude(a => a.Category)
            .Where(ah => ah.UserId == userId)
            .Where(ah => ah.StartTimestamp >= from && ah.StartTimestamp < to)
            .ToListAsync(ct);


        var windows = GenerateWindows(from, to);

        var response = new HistoryStackedBarsResponse
        {
            Windows = windows.Select(w => new HistoryWindow
            {
                WindowStart = w.Start,
                WindowEnd = w.End,
                Items = records
                    .Where(ah => ah.StartTimestamp >= w.Start && ah.StartTimestamp < w.End)
                    .GroupBy(ah => ah.ResolveGroupKey(req.GroupBy))
                    .Select(g => new HistoryGroupItem
                    {
                        GroupId = g.Key.Id,
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

    private static List<(DateTime Start, DateTime End)> GenerateWindows(
        DateTime from, DateTime to)
    {
        var windows = new List<(DateTime Start, DateTime End)>();

        for (var h = 0; h < 24; h++)
        {
            var start = from.AddHours(h);
            windows.Add((start, start.AddHours(1)));
        }

        return windows;
    }
}