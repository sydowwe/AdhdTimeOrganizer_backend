using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.bucketList.query;

public class GetSelectOptionsActivityBucketListProfileEndpoint(DbContext dbContext)
    : EndpointWithoutRequest<List<SelectOptionResponse>>
{
    public override void Configure()
    {
        Get("/activity-bucket-list-profile/all-options");
        Summary(s =>
        {
            s.Summary = "Get ActivityBucketListProfile select options";
            s.Response<List<SelectOptionResponse>>(200, "Success");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();

        var options = await dbContext.Set<ActivityBucketListProfile>()
            .AsNoTracking()
            .Where(p => p.Activity.UserId == userId)
            .OrderBy(p => p.Activity.Name)
            .Select(p => new SelectOptionResponse(p.Id, p.Activity.Name))
            .ToListAsync(ct);

        await Send.OkAsync(options, ct);
    }
}