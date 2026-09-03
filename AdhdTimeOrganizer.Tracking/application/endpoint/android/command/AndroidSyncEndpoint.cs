using System.Globalization;
using AdhdTimeOrganizer.Core.infrastructure.security;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.android;
using AdhdTimeOrganizer.Tracking.application.endpoint.extensions;
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

        // CQ-6: AdjustToUniversal *converts* an offset-bearing instant rather than re-labelling it, and
        // AssumeUniversal reads a naked one as UTC — which is what the field name promises. The previous
        // RoundtripKind preserved the client's offset, so "2026-09-01T10:00:00+02:00" parsed to the local
        // 10:00 and the SpecifyKind that followed stamped Utc on it without converting: a client being more
        // correct than the contract asks had every session stored two hours late, and every dashboard, band
        // and merged minute for it was silently wrong. Both styles yield a Utc-kind value, so nothing needs
        // to specify the kind afterwards. Invariant culture because these are a machine contract, not text
        // the server's locale has any say over.
        const DateTimeStyles instantStyles = DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal;

        // Parse and validate sessions
        var validSessions = new List<(string PackageName, string AppLabel, DateTime Start, DateTime End, long Duration)>();

        foreach (var item in req.Sessions)
        {
            if (!DateTime.TryParse(item.SessionStartUtc, CultureInfo.InvariantCulture, instantStyles, out var start) ||
                !DateTime.TryParse(item.SessionEndUtc, CultureInfo.InvariantCulture, instantStyles, out var end))
            {
                // CQ-22: the two instants are client-supplied free text and this is the one line that logs
                // them verbatim. AndroidSyncValidator bounds them to TrackingIngestLimits.MaxInstantStringLength
                // before the handler runs, so what reaches the log file is length-bounded.
                Logger.LogWarning("Android sync: skipping session with unparseable dates. Package={Package} Start={Start} End={End}",
                    item.PackageName, item.SessionStartUtc, item.SessionEndUtc);
                continue;
            }

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

        // Seeded from the stored rows, then grown by this batch — see the Add below.
        var seenKeys = existingKeys
            .Select(x => (x.PackageName, x.SessionStartUtc))
            .ToHashSet();

        var toInsert = new List<AndroidSessionData>();
        foreach (var session in validSessions)
        {
            // CQ-7: Add returns false for a key already seen, which now covers repeats *within* the batch as
            // well as rows already stored. UserId and DeviceId are fixed for the request, so (package, start)
            // is the rest of ix_android_session_data_user_id_device_id_package_name_session_. A phone that
            // re-uploads its buffer, or reports two rows for one app in the same second, previously put both
            // copies into AddRange, violated that index and 500ed the whole sync — and then retried the same
            // batch forever. The endpoint was idempotent across requests and not within one.
            if (!seenKeys.Add((session.PackageName, session.Start)))
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

            // CQ-11: seenKeys closes the in-batch and already-stored cases, but two syncs from the same
            // device in flight together can still both pass the read above. That last race is a 409 rather
            // than a 500 the phone re-posts unchanged — on the retry the rows are stored and skipped.
            if (!await this.TrySaveIngestBatchAsync(dbContext, ct))
                return;
        }

        await Send.ResponseAsync(new AndroidSyncResponse
        {
            Accepted = accepted,
            DuplicatesSkipped = duplicatesSkipped,
            SyncedUpToUtc = req.SyncedUpToUtc.ToString("O")
        }, cancellation: ct);
    }
}