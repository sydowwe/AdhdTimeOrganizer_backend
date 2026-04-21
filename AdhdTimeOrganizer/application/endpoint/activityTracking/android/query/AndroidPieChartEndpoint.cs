using AdhdTimeOrganizer.application.dto.request.activityTracking;
using AdhdTimeOrganizer.application.dto.response.activityTracking.android.dashboard;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.android.query;

public class AndroidPieChartEndpoint(AppDbContext db) : Endpoint<PieChartRequest, AndroidPieChartResponse>
{
    public override void Configure()
    {
        Post("/activity-tracking/android/pie-chart");
        Validator<PieChartValidator>();
    }

    public override async Task HandleAsync(PieChartRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var (from, to) = req.ToDateTimeRange();

        var periodData = await db.AndroidSessionDataEntries
            .Where(x => x.UserId == userId)
            .Where(x => x.SessionStartUtc >= from && x.SessionStartUtc < to)
            .ToListAsync(ct);

        var totalSeconds = periodData.Sum(x => x.DurationSeconds);
        var totalApps = periodData.Select(x => x.AppLabel).Distinct().Count();
        var totalSessions = periodData.Count;

        var appGroups = periodData
            .GroupBy(x => (x.PackageName, x.AppLabel))
            .Select(g => new AndroidAppPieData
            {
                PackageName = g.Key.PackageName,
                AppLabel = g.Key.AppLabel,
                Seconds = g.Sum(x => x.DurationSeconds),
                TotalSeconds = g.Sum(x => x.DurationSeconds)
            })
            .OrderByDescending(a => a.Seconds)
            .ToList();

        var minPercent = req.MinPercent ?? 1.0;
        List<AndroidAppPieData> result;

        if (totalSeconds > 0)
        {
            var minSeconds = totalSeconds * minPercent / 100.0;
            result = appGroups.Where(a => a.Seconds >= minSeconds).ToList();
        }
        else
        {
            result = appGroups;
        }

        var totals = new AndroidPieTotals
        {
            TotalSeconds = totalSeconds,
            TotalApps = totalApps,
            TotalSessions = totalSessions
        };

        await Send.ResponseAsync(new AndroidPieChartResponse { Apps = result, Totals = totals }, cancellation: ct);
    }
}
