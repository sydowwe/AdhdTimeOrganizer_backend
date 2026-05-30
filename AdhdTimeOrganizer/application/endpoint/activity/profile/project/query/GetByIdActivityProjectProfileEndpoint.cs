using AdhdTimeOrganizer.application.dto.response.activity.profile;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.domain.model.entity.activity.profile;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activity.profile.project.query;

public class GetByIdActivityProjectProfileEndpoint(AppDbContext dbContext)
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

        await Send.OkAsync(ActivityProjectProfileResponse.FromEntity(entity), ct);
    }
}
