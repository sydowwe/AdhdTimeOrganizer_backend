using AdhdTimeOrganizer.Core.infrastructure.security;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.heartbeat;
using AdhdTimeOrganizer.Tracking.application.endpoint.extensions;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.helper.unified;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.Tracking.domain.serviceContract;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.infrastructure.security;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.webExtension.command;

[AllowExtensionClients]
public class WebExtensionDataHeartbeatEndpoint(DbContext dbContext, ITrackerLastSeenStamp lastSeen)
    : Endpoint<WebExtensionHeartbeatRequest, int>
{
    public override void Configure()
    {
        Post("/activity-tracking/web-extension/heartbeat");
        Validator<WebExtensionHeartbeatValidator>();
        Summary(s =>
        {
            s.Summary = "Receive browser extension heartbeat activity data";
            s.Description = "Records web extension activity data from browser, creating or updating domain activity records";
            s.Response<int>(200, "Success - returns count of processed activities");
            s.Response(400, "Bad request");
        });
        // [AllowExtensionClients] only lifts the blanket DenyExtensionClients policy -- it grants
        // nothing. Without this the route falls back to the configurator's Roles("User","Admin","Root"),
        // so a plain web JWT could post browsing rows into the ledger. Its two ingest siblings
        // (DesktopActivityHeartbeatEndpoint, AndroidSyncEndpoint) carry the same pair.
        Policies(PortalAuthorizationPolicies.ActivityTracking);
    }

    public override async Task HandleAsync(WebExtensionHeartbeatRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        var processedCount = 0;

        // P2-04: first, before the window's rows are tracked — a validated heartbeat is the extension alive.
        await lastSeen.StampAsync(userId, TrackingSource.WebExtension, ct);

        // CQ-5: one read for the whole window, keyed by domain, instead of a FirstOrDefaultAsync per
        // activity inside the loop. This is the highest-frequency write path in the module — once a minute,
        // per user, per browser — and the round trips were linear in the batch. The unique index
        // (UserId, WindowStart, Domain, RecordDate) already serves this read, so it costs no new index and
        // guarantees the dictionary key is unique. Ordinal because that is how Postgres compares the
        // column; a case-insensitive comparer here would fold two domains the index keeps apart.
        var byDomain = (await dbContext.Set<WebExtensionActivityEntry>()
                .Where(x => x.UserId == userId && x.WindowStart == req.WindowStart)
                .ToListAsync(ct))
            .ToDictionary(x => x.Domain, StringComparer.Ordinal);

        foreach (var activity in req.Activities.Where(activity => activity.ActiveSeconds != 0 || activity.BackgroundSeconds != 0))
        {
            if (byDomain.TryGetValue(activity.Domain, out var existing))
            {
                // UPDATE existing record
                // Take the values from request (extension sends running totals, not deltas)
                existing.ActiveSeconds = activity.ActiveSeconds;
                existing.BackgroundSeconds = activity.BackgroundSeconds;

                // Update URL if provided (keep most recent)
                if (!string.IsNullOrEmpty(activity.Url))
                    existing.Url = activity.Url;

                // Update final flag (once true, stays true)
                if (req.IsFinal)
                    existing.IsFinal = true;
            }
            else
            {
                // INSERT new record
                var record = new WebExtensionActivityEntry
                {
                    UserId = userId,
                    WindowStart = req.WindowStart,
                    Domain = activity.Domain,
                    Url = activity.Url,
                    ActiveSeconds = activity.ActiveSeconds,
                    BackgroundSeconds = activity.BackgroundSeconds,
                    IsFinal = req.IsFinal
                };

                dbContext.Set<WebExtensionActivityEntry>().Add(record);

                // Registered before the save, so a domain repeated within one payload updates the row this
                // loop just added instead of inserting a second one and violating the unique index. The
                // extension sends running totals, so the last occurrence winning is the same rule the
                // update branch above already applies across requests.
                byDomain[record.Domain] = record;
            }

            processedCount++;
        }

        if (!await this.TrySaveIngestBatchAsync(dbContext, ct))
            return;

        await Send.ResponseAsync(processedCount, cancellation: ct);
    }
}