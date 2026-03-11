using AdhdTimeOrganizer.application.dto.request.activityTracking.android;
using AdhdTimeOrganizer.application.dto.response.activityTracking.android;
using AdhdTimeOrganizer.application.endpointGroups;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.infrastructure.security;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.android.command;

[AllowExtensionClients]
public class AndroidSyncEndpoint(AppDbContext dbContext) : Endpoint<AndroidSyncRequest, AndroidSyncResponse>
{
    public override void Configure()
    {
        Post("/sync");
        Validator<AndroidSyncValidator>();
        Group<ActivityTrackingAndroidGroup>();
    }

    public override async Task HandleAsync(AndroidSyncRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        var accepted = 0;
        var duplicatesSkipped = 0;

        // Parse and validate sessions
        var validSessions = new List<(string PackageName, string AppLabel, DateTime Start, DateTime End, long Duration)>();

        foreach (var item in req.Sessions)
        {
            if (!DateTime.TryParse(item.SessionStartUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var start) ||
                !DateTime.TryParse(item.SessionEndUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var end))
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
            await SendAsync(new AndroidSyncResponse
            {
                Accepted = 0,
                DuplicatesSkipped = 0,
                SyncedUpToUtc = req.SyncedUpToUtc.ToString("O")
            }, cancellation: ct);
            return;
        }

        // Query existing sessions to detect duplicates (Option B: bulk dedup)
        var earliestStart = validSessions.Min(s => s.Start);
        var existingKeys = await dbContext.AndroidSessionDataEntries
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
                DurationSeconds = session.Duration,
            });

            accepted++;
        }

        if (toInsert.Count > 0)
        {
            dbContext.AndroidSessionDataEntries.AddRange(toInsert);
            await dbContext.SaveChangesAsync(ct);
        }

        await SendAsync(new AndroidSyncResponse
        {
            Accepted = accepted,
            DuplicatesSkipped = duplicatesSkipped,
            SyncedUpToUtc = req.SyncedUpToUtc.ToString("O")
        }, cancellation: ct);
    }
}
