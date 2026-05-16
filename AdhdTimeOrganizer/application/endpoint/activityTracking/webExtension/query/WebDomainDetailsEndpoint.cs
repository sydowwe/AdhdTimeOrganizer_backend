using AdhdTimeOrganizer.application.dto.request.activityTracking;
using AdhdTimeOrganizer.application.dto.response.activityTracking;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.webExtension.query;

public class WebDomainDetailsEndpoint(AppDbContext db) : Endpoint<DomainDetailsRequest, DomainDetailsResponse>
{
    public override void Configure()
    {
        Get("/activity-tracking/web-extension/domain-details");
        Validator<DomainDetailsValidator>();
        Summary(s =>
        {
            s.Summary = "Get details and page breakdown for a single domain";
            s.Description = "Retrieves detailed activity breakdown for a specific domain, including page-level statistics and time distribution";
            s.Response<DomainDetailsResponse>(200, "Success");
            s.Response(400, "Bad request");
            s.Response(404, "Domain not found");
        });
    }

    public override async Task HandleAsync(DomainDetailsRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        // 1. Get all records for this domain in the time range
        var records = await db.WebExtensionActivityEntries
            .Where(x => x.UserId == userId)
            .Where(x => x.Domain == req.Domain)
            .Where(x => x.WindowStart >= req.From && x.WindowStart < req.To)
            .ToListAsync(ct);

        if (records.Count == 0)
        {
            AddError("Domain not found.");
            await Send.ErrorsAsync(404, ct);
            return;
        }

        // 2. Calculate totals
        var totalSeconds = records.Sum(x => x.ActiveSeconds + x.BackgroundSeconds);
        var activeSeconds = records.Sum(x => x.ActiveSeconds);
        var backgroundSeconds = records.Sum(x => x.BackgroundSeconds);

        // 3. Aggregate pages
        var pages = records
            .Where(x => !string.IsNullOrEmpty(x.Url))
            .GroupBy(x => x.Url)
            .Select(g => new PageVisitDto
            {
                Url = g.Key!,
                Path = ExtractPath(g.Key!),
                TotalSeconds = g.Sum(x => x.ActiveSeconds + x.BackgroundSeconds),
                ActiveSeconds = g.Sum(x => x.ActiveSeconds),
                BackgroundSeconds = g.Sum(x => x.BackgroundSeconds)
            })
            .OrderByDescending(x => x.TotalSeconds)
            .ToList();

        // 4. Build response
        var response = new DomainDetailsResponse
        {
            Domain = req.Domain,
            TotalSeconds = totalSeconds,
            ActiveSeconds = activeSeconds,
            BackgroundSeconds = backgroundSeconds,
            Entries = records.Count,
            Pages = pages
        };

        await Send.ResponseAsync(response, cancellation: ct);
    }

    private string ExtractPath(string url)
    {
        try
        {
            var uri = new Uri(url);
            var path = uri.PathAndQuery;

            // Return "/" if just the root
            if (string.IsNullOrEmpty(path) || path == "/")
                return "/";

            // Truncate very long paths
            if (path.Length > 100)
                return path.Substring(0, 97) + "...";

            return path;
        }
        catch
        {
            // If URL parsing fails, return as-is (truncated)
            return url.Length > 100 ? url.Substring(0, 97) + "..." : url;
        }
    }
}
