using AdhdTimeOrganizer.application.dto.request.activityTracking.heartbeat;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.infrastructure.security;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.webExtension.command;

[AllowExtensionClients]
public class WebExtensionDataHeartbeatEndpoint(AppDbContext dbContext) : Endpoint<WebExtensionHeartbeatRequest, int>
{
    public override void Configure()
    {
        Post("/activity-tracking/web-extension/heartbeat");
        Validator<WebExtensionHeartbeatValidator>();
    }

    public override async Task HandleAsync(WebExtensionHeartbeatRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        var processedCount = 0;

        foreach (var activity in req.Activities.Where(activity => activity.ActiveSeconds != 0 || activity.BackgroundSeconds != 0))
        {
            // Find existing record for this user + window + domain
            var existing = await dbContext.WebExtensionActivityEntries
                .FirstOrDefaultAsync(x =>
                        x.UserId == userId &&
                        x.WindowStart == req.WindowStart &&
                        x.Domain == activity.Domain,
                    ct);

            if (existing != null)
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
                    RecordDate = DateOnly.FromDateTime(req.WindowStart),
                    WindowStart = req.WindowStart,
                    Domain = activity.Domain,
                    Url = activity.Url,
                    ActiveSeconds = activity.ActiveSeconds,
                    BackgroundSeconds = activity.BackgroundSeconds,
                    IsFinal = req.IsFinal,
                };

                dbContext.WebExtensionActivityEntries.Add(record);
            }

            processedCount++;
        }

        await dbContext.SaveChangesAsync(ct);

        await Send.ResponseAsync(processedCount, cancellation: ct);
    }
}