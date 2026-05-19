using AdhdTimeOrganizer.application.dto.response.activity.profile;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.mapper.activity.profile;
using AdhdTimeOrganizer.domain.model.entity.activity.profile;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activity.profile.bucketList.query;

public class GetByIdActivityBucketListProfileEndpoint(AppDbContext dbContext, ActivityBucketListProfileMapper mapper)
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

        await Send.OkAsync(mapper.ToResponse(entity), ct);
    }
}
