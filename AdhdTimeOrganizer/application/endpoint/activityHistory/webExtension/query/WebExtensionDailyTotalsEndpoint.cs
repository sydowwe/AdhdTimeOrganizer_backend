using AdhdTimeOrganizer.application.dto.request;
using AdhdTimeOrganizer.application.dto.response.activityTracking;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.webExtension.query;

public class WebExtensionDailyTotalsEndpoint(AppDbContext dbContext)
    : Endpoint<WebExtensionDailyTotalsRequest, WebExtensionDailyTotalsResponse>
{
    public override void Configure()
    {
        Get("/activity-tracking/web-extension/daily-totals");
    }

    public override async Task HandleAsync(WebExtensionDailyTotalsRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var dayStart = req.Date.Date;  // Midnight UTC
        var dayEnd = dayStart.AddDays(1);

        var data = await dbContext.WebExtensionData
            .Where(x => x.UserId == userId)
            .Where(x => x.WindowStart >= dayStart && x.WindowStart < dayEnd)
            .GroupBy(x => x.Domain)
            .Select(g => new DomainTotal
            {
                Domain = g.Key,
                ActiveSeconds = g.Sum(x => x.ActiveSeconds),
                BackgroundSeconds = g.Sum(x => x.BackgroundSeconds)
            })
            .OrderByDescending(x => x.ActiveSeconds + x.BackgroundSeconds)
            .ToListAsync(ct);

        var response = new WebExtensionDailyTotalsResponse
        {
            Date = dayStart,
            TotalActiveSeconds = data.Sum(x => x.ActiveSeconds),
            TotalBackgroundSeconds = data.Sum(x => x.BackgroundSeconds),
            TopDomains = data.Take(10).ToList()  // Top 10
        };

        await SendAsync(response, cancellation: ct);
    }
}
