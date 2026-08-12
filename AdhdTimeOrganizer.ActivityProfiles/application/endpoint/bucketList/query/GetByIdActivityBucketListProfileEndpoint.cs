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

        await Send.OkAsync(ActivityBucketListProfileResponse.Projection(new[] { entity }.AsQueryable()).Single(), ct);
    }
}