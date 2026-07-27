using AdhdTimeOrganizer.domain.model.entity.activity.profile;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.application.endpoint.activity.profile.project.query;

public class GetSelectOptionsActivityProjectProfileEndpoint(AppDbContext dbContext)
    : EndpointWithoutRequest<List<SelectOptionResponse>>
{
    public override void Configure()
    {
        Get("/activity-project-profile/all-options");
        Summary(s =>
        {
            s.Summary = "Get ActivityProjectProfile select options";
            s.Response<List<SelectOptionResponse>>(200, "Success");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();

        var options = await dbContext.Set<ActivityProjectProfile>()
            .AsNoTracking()
            .Where(p => p.Activity.UserId == userId)
            .OrderBy(p => p.Activity.Name)
            .Select(p => new SelectOptionResponse(p.Id, p.Activity.Name))
            .ToListAsync(ct);

        await Send.OkAsync(options, ct);
    }
}