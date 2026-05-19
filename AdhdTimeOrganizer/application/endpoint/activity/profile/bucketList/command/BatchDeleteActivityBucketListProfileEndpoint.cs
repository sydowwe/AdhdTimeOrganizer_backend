using AdhdTimeOrganizer.application.dto.request.generic;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entity.activity.profile;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activity.profile.bucketList.command;

public class BatchDeleteActivityBucketListProfileEndpoint(AppDbContext dbContext) : Endpoint<IdListRequest>
{
    public override void Configure()
    {
        Post("/activity-bucket-list-profile/batch-delete");
        Summary(s =>
        {
            s.Summary = "Batch delete ActivityBucketListProfile";
            s.Response(204, "Success");
            s.Response(404, "One or more entities not found");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(IdListRequest req, CancellationToken ct)
    {
        try
        {
            var userId = User.GetId();
            var entities = new List<ActivityBucketListProfile>();

            foreach (var id in req.Ids)
            {
                var entity = await dbContext.Set<ActivityBucketListProfile>()
                    .Include(p => p.Activity)
                    .FirstOrDefaultAsync(p => p.Id == id, ct);

                if (entity is null || entity.Activity.UserId != userId)
                {
                    AddError("ActivityBucketListProfile not found.");
                    await Send.ErrorsAsync(404, ct);
                    return;
                }

                entities.Add(entity);
            }

            dbContext.Set<ActivityBucketListProfile>().RemoveRange(entities);
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
