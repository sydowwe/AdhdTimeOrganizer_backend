using AdhdTimeOrganizer.application.endpointGroups;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.domain.model.valueObject;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.desktop.query;

public class DesktopDistinctProcessesEndpoint(AppDbContext db) : EndpointWithoutRequest<List<TitleValueObject>>
{
    public override void Configure()
    {
        Get("/distinct-processes");
        Group<ActivityTrackingDesktopGroup>();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();

        var result = await db.DesktopActivityEntries
            .Where(x => x.UserId == userId)
            .GroupBy(x => x.ProcessName)
            .Select(g => new TitleValueObject
            {
                Value = g.Key,
                Title = g.Max(x => x.ProductName == "" ? null : x.ProductName) ?? g.Key
            })
            .OrderBy(x => x.Title)
            .ToListAsync(ct);

        await Send.ResponseAsync(result, cancellation: ct);
    }
}