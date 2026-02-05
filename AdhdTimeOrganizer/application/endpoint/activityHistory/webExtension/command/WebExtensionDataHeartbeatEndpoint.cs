using System.Security.Claims;
using AdhdTimeOrganizer.application.dto.request;
using AdhdTimeOrganizer.application.dto.response.activityTracking;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.infrastructure.security;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.webExtension.command;

[AllowExtensionClients]
public class WebExtensionDataHeartbeatEndpoint(AppDbContext dbContext) : Endpoint<WebExtensionHeartbeatRequest, int>
{
    public override void Configure()
    {
        Post("/activity-tracking/web-extension/heartbeat");
        AuthSchemes("ActivityTracking");
        Validator<WebExtensionHeartbeatValidator>();
    }

    public override async Task HandleAsync(WebExtensionHeartbeatRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        var processedCount = 0;

        foreach (var activity in req.Window.Activities.Where(activity => activity.ActiveSeconds != 0 || activity.BackgroundSeconds != 0))
        {
            // Find existing record for this user + window + domain
            var existing = await dbContext.WebExtensionData
                .FirstOrDefaultAsync(x =>
                        x.UserId == userId &&
                        x.WindowStart == req.Window.WindowStart &&
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
                if (req.Window.IsFinal)
                    existing.IsFinal = true;
            }
            else
            {
                // INSERT new record
                var record = new WebExtensionData
                {
                    UserId = userId,
                    WindowStart = req.Window.WindowStart,
                    Domain = activity.Domain,
                    Url = activity.Url,
                    ActiveSeconds = activity.ActiveSeconds,
                    BackgroundSeconds = activity.BackgroundSeconds,
                    IsFinal = req.Window.IsFinal,
                };

                dbContext.WebExtensionData.Add(record);
            }

            processedCount++;
        }

        await dbContext.SaveChangesAsync(ct);

        await SendAsync(processedCount, cancellation: ct);
    }
}