using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.bucketList.command;

public class DeleteActivityBucketListProfileEndpoint(DbContext dbContext) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/activity-bucket-list-profile/{id:long:required}");
        Summary(s =>
        {
            s.Summary = "Delete ActivityBucketListProfile";
            s.Response(204, "Success");
            s.Response(404, "Not found");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        try
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

            dbContext.Set<ActivityBucketListProfile>().Remove(entity);
            await dbContext.SaveChangesAsync(ct);
            await Send.NoContentAsync(ct);
        }
        catch (Exception ex)
        {
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(400, ct);
        }
    }
}