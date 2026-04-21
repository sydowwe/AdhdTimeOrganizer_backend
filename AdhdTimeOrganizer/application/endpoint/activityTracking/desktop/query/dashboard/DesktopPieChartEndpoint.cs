using AdhdTimeOrganizer.application.dto.request.activityTracking;
using AdhdTimeOrganizer.application.dto.response.activityTracking.desktop.dashboard.pieChart;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.desktop.query.dashboard;

public class DesktopPieChartEndpoint(AppDbContext db) : Endpoint<PieChartRequest, DesktopPieChartResponse>
{
    public override void Configure()
    {
        Post("/activity-tracking/desktop/pie-chart");
        Validator<PieChartValidator>();
    }

    public override async Task HandleAsync(PieChartRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var (from, to) = req.ToDateTimeRange();

        var periodData = await db.DesktopActivityEntries
            .Where(x => x.UserId == userId)
            .Where(x => x.WindowStart >= from && x.WindowStart < to)
            .ToListAsync(ct);

        var totalSeconds = periodData.Sum(x => x.ActiveSeconds + x.BackgroundSeconds);

        var processGroups = periodData
            .GroupBy(x => x.ProcessName)
            .Select(g => new DesktopPieDataDto
            {
                ProcessName = g.Key,
                ProductName = g.Where(x => !string.IsNullOrEmpty(x.ProductName))
                    .Select(x => x.ProductName)
                    .FirstOrDefault() ?? g.Key,
                ActiveSeconds = g.Sum(x => x.ActiveSeconds),
                BackgroundSeconds = g.Sum(x => x.BackgroundSeconds),
                TotalSeconds = g.Sum(x => x.ActiveSeconds + x.BackgroundSeconds),
                WindowTitles = g
                    .Where(x => !string.IsNullOrEmpty(x.WindowTitle))
                    .Select(x => x.WindowTitle)
                    .Distinct()
                    .ToList(),
                Entries = g.Count()
            })
            .OrderByDescending(x => x.TotalSeconds)
            .ToList();

        List<DesktopPieDataDto> result;

        if (req.MinPercent.HasValue && totalSeconds > 0)
        {
            var minSeconds = (totalSeconds * req.MinPercent.Value) / 100.0;
            var above = processGroups.Where(x => x.TotalSeconds >= minSeconds).ToList();
            var below = processGroups.Where(x => x.TotalSeconds < minSeconds).ToList();

            result = above;

            if (below.Count > 0)
            {
                result.Add(new DesktopPieDataDto
                {
                    ProcessName = "Other",
                    ProductName = "Other",
                    ActiveSeconds = below.Sum(x => x.ActiveSeconds),
                    BackgroundSeconds = below.Sum(x => x.BackgroundSeconds),
                    TotalSeconds = below.Sum(x => x.TotalSeconds),
                    WindowTitles = below.SelectMany(x => x.WindowTitles).Distinct().ToList(),
                    Entries = below.Sum(x => x.Entries)
                });
            }
        }
        else
        {
            result = processGroups;
        }

        var totals = new DesktopDayTotalsResponse
        {
            TotalSeconds = periodData.Sum(x => x.ActiveSeconds + x.BackgroundSeconds),
            ActiveSeconds = periodData.Sum(x => x.ActiveSeconds),
            BackgroundSeconds = periodData.Sum(x => x.BackgroundSeconds),
            TotalProcesses = periodData.Select(x => x.ProcessName).Distinct().Count(),
            TotalWindowTitles = periodData.Where(x => x.WindowTitle != null).Select(x => x.WindowTitle).Distinct().Count(),
            TotalEntries = periodData.Count
        };

        await Send.ResponseAsync(new DesktopPieChartResponse { Processes = result, Totals = totals }, cancellation: ct);
    }
}
