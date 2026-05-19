using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.domain.model.entity.activity.profile;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activity.profile.backlog.query;

public class GetSelectOptionsActivityBacklogProfileEndpoint(AppDbContext dbContext)
    : EndpointWithoutRequest<List<SelectOptionResponse>>
{
    public override void Configure()
    {
        Get("/activity-backlog-profile/all-options");
        Summary(s =>
        {
            s.Summary = "Get ActivityBacklogProfile select options";
            s.Response<List<SelectOptionResponse>>(200, "Success");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();

        var options = await dbContext.Set<ActivityBacklogProfile>()
            .AsNoTracking()
            .Where(p => p.Activity.UserId == userId)
            .OrderBy(p => p.Activity.Name)
            .Select(p => new SelectOptionResponse(p.Id, p.Activity.Name))
            .ToListAsync(ct);

        await Send.OkAsync(options, ct);
    }
}
