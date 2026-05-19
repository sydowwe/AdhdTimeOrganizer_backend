using AdhdTimeOrganizer.application.dto.request.activity.profile;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.mapper.activity.profile;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activity.profile;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activity.profile.bucketList.command;

public class UpdateActivityBucketListProfileEndpoint(AppDbContext dbContext, ActivityBucketListProfileMapper mapper)
    : Endpoint<ActivityBucketListProfileRequest>
{
    public override void Configure()
    {
        Put("/activity-bucket-list-profile/{id:long:required}");
        Validator<UpdateActivityBucketListProfileValidator>();
        Summary(s =>
        {
            s.Summary = "Update ActivityBucketListProfile";
            s.Response(204, "Success");
            s.Response(404, "Not found");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(ActivityBucketListProfileRequest req, CancellationToken ct)
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

            mapper.UpdateEntity(req, entity);
            dbContext.Set<ActivityBucketListProfile>().Update(entity);
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
