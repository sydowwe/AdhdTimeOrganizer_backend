using AdhdTimeOrganizer.ActivityProfiles.application.dto.response;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.bucketList.query;

public class GetByIdActivityBucketListProfileEndpoint(DbContext dbContext)
    : EndpointWithoutRequest<ActivityBucketListProfileResponse>
{
    public override void Configure()
    {
        Get("/activity-bucket-list-profile/{id:long:required}");
        Summary(s =>
        {
            s.Summary = "Get ActivityBucketListProfile by ID";
            s.Response<ActivityBucketListProfileResponse>(200, "Success");
            s.Response(404, "Not found");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<long>("id");
        var userId = User.GetId();

        var entity = await dbContext.Set<ActivityBucketListProfile>()
            .Include(p => p.Activity)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (entity is null || entity.Activity.UserId != userId)
        {
            AddError("ActivityBucketListProfile not found.");
            await Send.ErrorsAsync(404, ct);
            return;
        }

        // Projection runs over an in-memory array and so cannot reach the anchors. Overlay them from a
        // second read rather than let this route answer isAnchored: false for an entry the grid shows as
        // done.
        var anchorId = await dbContext.Set<MemoryAnchor>()
            .Where(m => m.UserId == userId && m.ActivityId == entity.ActivityId)
            .OrderByDescending(m => m.AnchorYear)
            .ThenByDescending(m => m.AnchorMonth)
            .ThenByDescending(m => m.Id)
            .Select(m => (long?)m.Id)
            .FirstOrDefaultAsync(ct);

        var response = ActivityBucketListProfileResponse.Projection(new[] { entity }.AsQueryable()).Single()
            with
            {
                IsAnchored = anchorId is not null,
                MemoryAnchorId = anchorId
            };

        await Send.OkAsync(response, ct);
    }
}