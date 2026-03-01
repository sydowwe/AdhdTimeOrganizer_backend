using AdhdTimeOrganizer.application.dto.request.activityTracking;
using AdhdTimeOrganizer.application.dto.response.activityTracking.pieChart;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.webExtension.query;

public class PieChartEndpoint(AppDbContext db) : Endpoint<PieChartRequest, PieChartResponse>
{
    public override void Configure()
    {
        Post("/activity-tracking/web-extension/pie-chart");
        Validator<PieChartValidator>();
    }

    public override async Task HandleAsync(PieChartRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var (from, to) = req.ToDateTimeRange();

        // Get all data for the period
        var periodData = await db.WebExtensionData
            .Where(x => x.UserId == userId)
            .Where(x => x.WindowStart >= from && x.WindowStart < to)
            .ToListAsync(ct);

        // Calculate total seconds for percentage calculations
        var totalSeconds = periodData.Sum(x => x.ActiveSeconds + x.BackgroundSeconds);

        // Group by domain and aggregate
        var domainGroups = periodData
            .GroupBy(x => x.Domain)
            .Select(g => new DomainPieDataDto
            {
                Domain = g.Key,
                ActiveSeconds = g.Sum(x => x.ActiveSeconds),
                BackgroundSeconds = g.Sum(x => x.BackgroundSeconds),
                TotalSeconds = g.Sum(x => x.ActiveSeconds + x.BackgroundSeconds),
                Pages = g.Where(x => !string.IsNullOrEmpty(x.Url))
                    .Select(x => x.Url!)
                    .Distinct()
                    .ToList(),
                Entries = g.Count()
            })
            .OrderByDescending(x => x.TotalSeconds)
            .ToList();

        // Filter by MinPercent if specified
        List<DomainPieDataDto> result;

        if (req.MinPercent.HasValue && totalSeconds > 0)
        {
            var minSeconds = (totalSeconds * req.MinPercent.Value) / 100.0;
            var domainsAboveThreshold = domainGroups
                .Where(x => x.TotalSeconds >= minSeconds)
                .ToList();

            var domainsBelowThreshold = domainGroups
                .Where(x => x.TotalSeconds < minSeconds)
                .ToList();

            result = domainsAboveThreshold;

            // Add "Other" category if there are domains below threshold
            if (domainsBelowThreshold.Any())
            {
                var otherCategory = new DomainPieDataDto
                {
                    Domain = "Other",
                    ActiveSeconds = domainsBelowThreshold.Sum(x => x.ActiveSeconds),
                    BackgroundSeconds = domainsBelowThreshold.Sum(x => x.BackgroundSeconds),
                    TotalSeconds = domainsBelowThreshold.Sum(x => x.TotalSeconds),
                    Pages = domainsBelowThreshold
                        .SelectMany(x => x.Pages)
                        .Distinct()
                        .ToList(),
                    Entries = domainsBelowThreshold.Sum(x => x.Entries)
                };
                result.Add(otherCategory);
            }
        }
        else
        {
            // No filter, return all domains
            result = domainGroups;
        }

        var totals = new DayTotalsResponse
        {
            TotalSeconds = periodData.Sum(x => x.ActiveSeconds + x.BackgroundSeconds),
            ActiveSeconds = periodData.Sum(x => x.ActiveSeconds),
            BackgroundSeconds = periodData.Sum(x => x.BackgroundSeconds),
            TotalDomains = periodData.Select(x => x.Domain).Distinct().Count(),
            TotalPages = periodData.Where(x => x.Url != null).Select(x => x.Url).Distinct().Count(),
            TotalEntries = periodData.Count
        };

        var response = new PieChartResponse
        {
            Domains = result,
            Totals = totals
        };

        await SendAsync(response, cancellation: ct);
    }
}
