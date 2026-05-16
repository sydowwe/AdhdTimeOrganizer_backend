using AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.application.dto.response.activityTracking.desktop.dashboard;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.desktop.query.dashboard;

public class DesktopProcessDetailsEndpoint(AppDbContext db) : Endpoint<DesktopProcessDetailsRequest, DesktopProcessDetailsResponse>
{
    public override void Configure()
    {
        Get("/activity-tracking/desktop/process-details");
        Summary(s =>
        {
            s.Summary = "Get detailed desktop process usage statistics";
            s.Description = "Returns breakdown of a specific process by window titles and monitors for a given date range";
            s.Response<DesktopProcessDetailsResponse>(200, "Success");
            s.Response(400, "Bad request");
            s.Response(404, "Process not found");
        });
        Validator<DesktopProcessDetailsValidator>();
    }

    public override async Task HandleAsync(DesktopProcessDetailsRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var records = await db.DesktopActivityEntries
            .Where(x => x.UserId == userId)
            .Where(x => x.ProcessName == req.ProcessName)
            .Where(x => x.WindowStart >= req.From && x.WindowStart < req.To)
            .ToListAsync(ct);

        if (records.Count == 0)
        {
            AddError("Process not found.");
            await Send.ErrorsAsync(404, ct);
            return;
        }

        var productName = records
            .Where(x => !string.IsNullOrEmpty(x.ProductName))
            .Select(x => x.ProductName)
            .FirstOrDefault() ?? req.ProcessName;

        var windowTitles = records
            .Where(x => !string.IsNullOrEmpty(x.WindowTitle))
            .GroupBy(x => x.WindowTitle)
            .Select(g => new DesktopWindowTitleVisitDto
            {
                WindowTitle = g.Key,
                TotalSeconds = g.Sum(x => x.ActiveSeconds + x.BackgroundSeconds),
                ActiveSeconds = g.Sum(x => x.ActiveSeconds),
                BackgroundSeconds = g.Sum(x => x.BackgroundSeconds),
                FullscreenSeconds = g.Where(x => x.IsFullscreen).Sum(x => x.ActiveSeconds),
                SoundSeconds = g.Where(x => x.IsPlayingSound).Sum(x => x.ActiveSeconds)
            })
            .OrderByDescending(x => x.TotalSeconds)
            .ToList();

        var monitorBreakdown = records
            .GroupBy(x => x.ActiveMonitor)
            .Select(g => new DesktopMonitorUsageDto
            {
                Monitor = g.Key,
                ActiveSeconds = g.Sum(x => x.ActiveSeconds)
            })
            .OrderBy(x => x.Monitor)
            .ToList();

        var response = new DesktopProcessDetailsResponse
        {
            ProcessName = req.ProcessName,
            ProductName = productName,
            TotalSeconds = records.Sum(x => x.ActiveSeconds + x.BackgroundSeconds),
            ActiveSeconds = records.Sum(x => x.ActiveSeconds),
            BackgroundSeconds = records.Sum(x => x.BackgroundSeconds),
            FullscreenSeconds = records.Where(x => x.IsFullscreen).Sum(x => x.ActiveSeconds),
            SoundSeconds = records.Where(x => x.IsPlayingSound).Sum(x => x.ActiveSeconds),
            MonitorBreakdown = monitorBreakdown,
            Entries = records.Count,
            WindowTitles = windowTitles
        };

        await Send.ResponseAsync(response, cancellation: ct);
    }
}
