using System.Globalization;
using AdhdTimeOrganizer.Core.infrastructure.security;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.android;
using AdhdTimeOrganizer.Tracking.application.endpointGroups;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.Tracking.infrastructure.persistence.retention;
using FastEndpoints;
using Microsoft.Extensions.Options;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.infrastructure.security;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.android.command;

[AllowExtensionClients]
public class AndroidSyncEndpoint(DbContext dbContext, IOptions<ActivityTrackingRetentionOptions> retentionOptions)
    : Endpoint<AndroidSyncRequest, AndroidSyncResponse>
{
    public override void Configure()
    {
        Post("/sync");
        Summary(s => s.Description = "Sync Android session data from mobile device");
        Validator<AndroidSyncValidator>();
        Group<ActivityTrackingAndroidGroup>();
        Policies(PortalAuthorizationPolicies.ActivityTracking);
    }

    public override async Task HandleAsync(AndroidSyncRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        var accepted = 0;
        var duplicatesSkipped = 0;

        // SEC-5: the instants come from a phone's clock and are stored permanently, so they are bounded
        // here — the validator can only bound the stated DurationSeconds, not the span the two strings
        // actually describe. A session outside these bounds is dropped with the other unusable ones rather
        // than failing the whole sync: the batch is a backlog upload, and one bad row must not strand the
        // rest of the device's history.
        var now = DateTime.UtcNow;
        var latestAcceptable = now + TrackingIngestLimits.MaxClockSkew;
        var earliestAcceptable = retentionOptions.Value.CutoffUtc(now);
        var maxSpan = TimeSpan.FromSeconds(TrackingIngestLimits.MaxAndroidSessionSeconds);

        // Parse and validate sessions
        var validSessions = new List<(string PackageName, string AppLabel, DateTime Start, DateTime End, long Duration)>();

        foreach (var item in req.Sessions)
        {
            if (!DateTime.TryParse(item.SessionStartUtc, null, DateTimeStyles.RoundtripKind, out var start) ||
                !DateTime.TryParse(item.SessionEndUtc, null, DateTimeStyles.RoundtripKind, out var end))
            {
                Logger.LogWarning("Android sync: skipping session with unparseable dates. Package={Package} Start={Start} End={End}",
                    item.PackageName, item.SessionStartUtc, item.SessionEndUtc);
                continue;
            }

            start = DateTime.SpecifyKind(start, DateTimeKind.Utc);
            end = DateTime.SpecifyKind(end, DateTimeKind.Utc);

            if (end <= start)
            {
                Logger.LogWarning("Android sync: skipping session where end <= start. Package={Package}", item.PackageName);
                continue;
            }

            if (end - start > maxSpan)
            {
                Logger.LogWarning("Android sync: skipping session longer than the {MaxHours}h bound. Package={Package} Seconds={Seconds}",
                    maxSpan.TotalHours, item.PackageName, (long)(end - start).TotalSeconds);
                continue;
            }

            if (start > latestAcceptable || end > latestAcceptable)
            {
                Logger.LogWarning("Android sync: skipping session in the future. Package={Package}", item.PackageName);
                continue;
            }

            // Past the retention horizon the purge job would delete it on its next run anyway, so storing
            // it only creates a row that reads as history nobody can explain.
            if (start < earliestAcceptable)
            {
                Logger.LogWarning("Android sync: skipping session older than the retention horizon. Package={Package}", item.PackageName);
                continue;
            }

            var expectedDuration = (long)(end - start).TotalSeconds;
            if (Math.Abs(expectedDuration - item.DurationSeconds) > 2)
            {
                Logger.LogWarning("Android sync: skipping session with mismatched duration. Package={Package} Expected={Expected} Got={Got}",
                    item.PackageName, expectedDuration, item.DurationSeconds);
                continue;
            }

            validSessions.Add((item.PackageName, item.AppLabel, start, end, item.DurationSeconds));
        }

        if (validSessions.Count == 0)
        {
            await Send.ResponseAsync(new AndroidSyncResponse
            {
                Accepted = 0,
                DuplicatesSkipped = 0,
                SyncedUpToUtc = req.SyncedUpToUtc.ToString("O")
            }, cancellation: ct);
            return;
        }

        // Query existing sessions to detect duplicates (Option B: bulk dedup)
        var earliestStart = validSessions.Min(s => s.Start);
        var existingKeys = await dbContext.Set<AndroidSessionData>()
            .Where(x => x.UserId == userId && x.DeviceId == req.DeviceId && x.SessionStartUtc >= earliestStart)
            .Select(x => new { x.PackageName, x.SessionStartUtc })
            .ToListAsync(ct);

        var existingSet = existingKeys
            .Select(x => (x.PackageName, x.SessionStartUtc))
            .ToHashSet();

        var toInsert = new List<AndroidSessionData>();
        foreach (var session in validSessions)
        {
            if (existingSet.Contains((session.PackageName, session.Start)))
            {
                duplicatesSkipped++;
                continue;
            }

            toInsert.Add(new AndroidSessionData
            {
                UserId = userId,
                DeviceId = req.DeviceId,
                PackageName = session.PackageName,
                AppLabel = session.AppLabel,
                SessionStartUtc = session.Start,
                SessionEndUtc = session.End,
                DurationSeconds = session.Duration
            });

            accepted++;
        }

        if (toInsert.Count > 0)
        {
            dbContext.Set<AndroidSessionData>().AddRange(toInsert);
            await dbContext.SaveChangesAsync(ct);
        }

        await Send.ResponseAsync(new AndroidSyncResponse
        {
            Accepted = accepted,
            DuplicatesSkipped = duplicatesSkipped,
            SyncedUpToUtc = req.SyncedUpToUtc.ToString("O")
        }, cancellation: ct);
    }
}