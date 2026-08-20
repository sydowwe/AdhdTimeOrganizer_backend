using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.android.dashboard;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.android.query;

public class AndroidPieChartEndpoint(DbContext db, IUserTimeZoneResolver timeZones) : Endpoint<PieChartRequest, AndroidPieChartResponse>
{
    public override void Configure()
    {
        Post("/activity-tracking/android/pie-chart");
        Summary(s =>
        {
            s.Summary = "Get Android app usage pie chart data";
            s.Description = "Returns a breakdown of app usage time by package/label for a given date range, filtered by minimum percentage threshold";
            s.Response<AndroidPieChartResponse>(200, "Success");
            s.Response(400, "Bad request");
        });
        Validator<PieChartValidator>();
    }

    public override async Task HandleAsync(PieChartRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var windows = req.ToDailyWindows(await timeZones.GetAsync(userId, ct));

        // One range predicate over the span's outer envelope, then the gaps between the daily windows
        // are dropped in memory — an envelope read alone would fold in every night the user's
        // time-of-day window excludes.
        var from = windows.EnvelopeFrom;
        var to = windows.EnvelopeTo;

        var periodData = windows.Restrict(
            await db.Set<AndroidSessionData>()
                .Where(x => x.UserId == userId)
                .Where(x => x.SessionStartUtc >= from && x.SessionStartUtc < to)
                .ToListAsync(ct),
            x => x.SessionStartUtc);

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