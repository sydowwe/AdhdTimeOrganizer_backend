using AdhdTimeOrganizer.ActivityProfiles.application.dto.response;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.backlog.query;

public class GetByIdActivityBacklogProfileEndpoint(DbContext dbContext)
    : EndpointWithoutRequest<ActivityBacklogProfileResponse>
{
    public override void Configure()
    {
        Get("/activity-backlog-profile/{id:long:required}");
        Summary(s =>
        {
            s.Summary = "Get ActivityBacklogProfile by ID";
            s.Response<ActivityBacklogProfileResponse>(200, "Success");
            s.Response(404, "Not found");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<long>("id");
        var userId = User.GetId();

        var entity = await dbContext.Set<ActivityBacklogProfile>()
            .Include(p => p.Activity)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (entity is null || entity.Activity.UserId != userId)
        {
            AddError("ActivityBacklogProfile not found.");
            await Send.ErrorsAsync(404, ct);
            return;
        }

        // Projection runs over an in-memory array and so cannot reach the anchors. Overlay them from a
        // second read rather than let this route answer isAnchored: false for an entry the grid shows as
        // done. Repeatable entries are never anchored -- same rule as ProjectionWithAnchors.
        long? anchorId = entity.IsRepeatable
            ? null
            : await dbContext.Set<MemoryAnchor>()
                .Where(m => m.UserId == userId && m.ActivityId == entity.ActivityId)
                .OrderByDescending(m => m.AnchorYear)
                .ThenByDescending(m => m.AnchorMonth)
                .ThenByDescending(m => m.Id)
                .Select(m => (long?)m.Id)
                .FirstOrDefaultAsync(ct);

        var response = ActivityBacklogProfileResponse.Projection(new[] { entity }.AsQueryable()).Single()
            with
            {
                IsAnchored = anchorId is not null,
                MemoryAnchorId = anchorId
            };

        await Send.OkAsync(response, ct);
    }
}