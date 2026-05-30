using AdhdTimeOrganizer.application.dto.response.activity.profile;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.domain.model.entity.activity.profile;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activity.profile.backlog.query;

public class GetByIdActivityBacklogProfileEndpoint(AppDbContext dbContext)
    : EndpointWithoutRequest<ActivityBacklogProfileResponse>
{
    public override void Configure()
    {
        Get("/activity-backlog-profile/{id:long:required}");
        Summary(s =>
        {
            s.Summary = "Get ActivityBacklogProfile by ID";
            s.Response<ActivityBacklogProfileResponse>(200, "Success");
            s.Response(404, "Not found");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<long>("id");
        var userId = User.GetId();

        var entity = await dbContext.Set<ActivityBacklogProfile>()
            .Include(p => p.Activity)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (entity is null || entity.Activity.UserId != userId)
        {
            AddError("ActivityBacklogProfile not found.");
            await Send.ErrorsAsync(404, ct);
            return;
        }

        await Send.OkAsync(ActivityBacklogProfileResponse.FromEntity(entity), ct);
    }
}
