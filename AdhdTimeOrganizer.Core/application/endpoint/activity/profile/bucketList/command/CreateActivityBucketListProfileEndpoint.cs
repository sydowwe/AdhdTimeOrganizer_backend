using AdhdTimeOrganizer.Core.application.dto.request.activity.profile;
using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.Core.domain.model.entity.activity.profile;
using FastEndpoints;
using Sydowwe.Framework.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.profile.bucketList.command;

public class CreateActivityBucketListProfileEndpoint(DbContext dbContext)
    : Endpoint<ActivityBucketListProfileRequest, long>
{
    public override void Configure()
    {
        Post("/activity-bucket-list-profile");
        Validator<CreateActivityBucketListProfileValidator>();
        Summary(s =>
        {
            s.Summary = "Create ActivityBucketListProfile";
            s.Response<long>(201, "Created");
            s.Response(400, "Bad request");
            s.Response(404, "Activity not found");
        });
    }

    public override async Task HandleAsync(ActivityBucketListProfileRequest req, CancellationToken ct)
    {
        try
        {
            var entity = req.ToEntity;
            await dbContext.Set<ActivityBucketListProfile>().AddAsync(entity, ct);
            await dbContext.SaveChangesAsync(ct);
            await Send.ResponseAsync(entity.Id, 201, ct);
        }
        catch (Exception ex)
        {
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(400, ct);
        }
    }
}