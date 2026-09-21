using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.android.dashboard;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.helper;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.android.query;

/// <summary>
/// The android sibling of <c>DesktopProcessDetailsEndpoint</c> (B23): what the pie's "View details for
/// {app}" card opens. Same envelope, same optional daily window, same 404 on an empty read — keyed on
/// the package instead of the process, and over real session rows, so the panel can say how many
/// sessions, how long the mean and longest were, and on which devices and days.
/// </summary>
public class AndroidAppDetailsEndpoint(DbContext db, IUserTimeZoneResolver timeZones)
    : Endpoint<AndroidAppDetailsRequest, AndroidAppDetailsResponse>
{
    public override void Configure()
    {
        Get("/activity-tracking/android/app-details");
        Summary(s =>
        {
            s.Summary = "Get detailed Android app usage statistics";
            s.Description = "Returns session totals plus per-device and per-day breakdowns of a specific app for a given date range";
            s.Response<AndroidAppDetailsResponse>(200, "Success");
            s.Response(400, "Bad request");
            s.Response(404, "App not found");
        });
        Validator<AndroidAppDetailsValidator>();
    }

    public override async Task HandleAsync(AndroidAppDetailsRequest req, CancellationToken ct)
    {
        // The pie's folded tail is not an app. The client refuses to select it; if it asks anyway the
        // answer is the same 404 an unknown package gets, never a resolution to the rows that were folded.
        if (req.PackageName == FoldedRow.Sentinel)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var userId = User.GetId();
        var timeZone = await timeZones.GetAsync(userId, ct);

        var sessions = await db.Set<AndroidSessionData>()
            .Where(x => x.UserId == userId)
            .Where(x => x.PackageName == req.PackageName)
            .Where(x => x.SessionStartUtc >= req.From && x.SessionStartUtc < req.To)
            .ToListAsync(ct);

        // Same rule as desktop's entries: a session that starts inside one of the daily windows counts
        // whole, one that starts outside does not count at all. Without the mask the envelope is
        // continuous and the panel would over-count against the slice it was opened from.
        var windows = req.ToDailyWindows(timeZone);
        if (windows is not null)
            sessions = windows.Restrict(sessions, x => x.SessionStartUtc);

        if (sessions.Count == 0)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var appLabel = sessions
            .Where(x => !string.IsNullOrEmpty(x.AppLabel))
            .Select(x => x.AppLabel)
            .FirstOrDefault() ?? req.PackageName;

        var totalSeconds = sessions.Sum(x => x.DurationSeconds);

        var devices = sessions
            .GroupBy(x => x.DeviceId)
            .Select(g => new AndroidDeviceUsageDto
            {
                DeviceId = g.Key,
                TotalSeconds = g.Sum(x => x.DurationSeconds),
                Sessions = g.Count()
            })
            .OrderByDescending(x => x.TotalSeconds)
            .ToList();

        // Days on the user's clock, not UTC: a 23:30 session in Bratislava is that day's, and bucketing it
        // by its UTC date would move it to the day before.
        var days = sessions
            .GroupBy(x => DateOnly.FromDateTime(WallClockZone.FromUtc(x.SessionStartUtc, timeZone)))
            .Select(g => new AndroidDayUsageDto
            {
                Date = g.Key,
                TotalSeconds = g.Sum(x => x.DurationSeconds),
                Sessions = g.Count()
            })
            .OrderBy(x => x.Date)
            .ToList();

        var response = new AndroidAppDetailsResponse
        {
            PackageName = req.PackageName,
            AppLabel = appLabel,
            TotalSeconds = totalSeconds,
            Sessions = sessions.Count,
            FirstUseUtc = sessions.Min(x => x.SessionStartUtc),
            LastUseUtc = sessions.Max(x => x.SessionEndUtc),
            MeanSessionSeconds = totalSeconds / sessions.Count,
            LongestSessionSeconds = sessions.Max(x => x.DurationSeconds),
            Devices = devices,
            Days = days
        };

        await Send.ResponseAsync(response, cancellation: ct);
    }

    private async Task SendNotFoundAsync(CancellationToken ct)
    {
        AddError("App not found.");
        await Send.ErrorsAsync(404, ct);
    }
}
