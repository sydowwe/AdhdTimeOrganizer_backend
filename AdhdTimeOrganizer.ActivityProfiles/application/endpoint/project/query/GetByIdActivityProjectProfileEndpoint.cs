using AdhdTimeOrganizer.ActivityProfiles.application.dto.response;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.ActivityProfiles.application.endpoint.project.query;

public class GetByIdActivityProjectProfileEndpoint(DbContext dbContext)
    : EndpointWithoutRequest<ActivityProjectProfileResponse>
{
    public override void Configure()
    {
        Get("/activity-project-profile/{id:long:required}");
        Summary(s =>
        {
            s.Summary = "Get ActivityProjectProfile by ID";
            s.Response<ActivityProjectProfileResponse>(200, "Success");
            s.Response(404, "Not found");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<long>("id");
        var userId = User.GetId();

        var entity = await dbContext.Set<ActivityProjectProfile>()
            .Include(p => p.Activity)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (entity is null || entity.Activity.UserId != userId)
        {
            AddError("ActivityProjectProfile not found.");
            await Send.ErrorsAsync(404, ct);
            return;
        }

        await Send.OkAsync(ActivityProjectProfileResponse.Projection(new[] { entity }.AsQueryable()).Single(), ct);
    }
}