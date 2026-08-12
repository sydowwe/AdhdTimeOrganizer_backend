using AdhdTimeOrganizer.History.application.dto.request.history;
using AdhdTimeOrganizer.History.application.dto.response.activityHistory;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.History.application.endpoint.activityHistory.activityHistory.query;

/// <summary>
/// Logged-time totals per activity id, across all of the caller's history. Exists because the pie-chart
/// dashboard endpoint groups by activity <em>name</em>, and names are not unique — a caller holding an
/// activity id cannot match a pie slice back to it reliably.
/// </summary>
public class AggregateByActivityActivityHistoryEndpoint(DbContext db)
    : Endpoint<ActivityHistoryAggregateByActivityRequest, List<ActivityHistoryAggregateByActivityResponse>>
{
    public override void Configure()
    {
        Post("/activity-history/aggregate-by-activity");
    }

    public override async Task HandleAsync(ActivityHistoryAggregateByActivityRequest req, CancellationToken ct)
    {
        var activityIds = req.ActivityIds.Distinct().ToList();

        if (activityIds.Count == 0)
        {
            await Send.ResponseAsync([], cancellation: ct);
            return;
        }

        var userId = User.GetId();

        // Summed in memory, as everywhere else in this slice: Length is an IntTime value object behind a
        // converter, so its TotalSeconds is not translatable to SQL. Only the two columns the aggregate
        // needs are projected, so the transfer is a pair of ints per row rather than whole entities.
        var rows = await db.Set<ActivityHistory>()
            .Where(ah => ah.UserId == userId && activityIds.Contains(ah.ActivityId))
            .Select(ah => new { ah.ActivityId, ah.Length })
            .ToListAsync(ct);

        var aggregates = rows
            .GroupBy(r => r.ActivityId)
            .Select(g => new ActivityHistoryAggregateByActivityResponse
            {
                ActivityId = g.Key,
                TotalSeconds = g.Sum(r => (long)r.Length.TotalSeconds),
                EntryCount = g.Count()
            })
            .OrderByDescending(a => a.TotalSeconds)
            .ToList();

        await Send.ResponseAsync(aggregates, cancellation: ct);
    }
}
