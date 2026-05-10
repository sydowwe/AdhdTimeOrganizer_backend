using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.mapper.user;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.user.plannerSettings;

public class GetPlannerSettingsEndpoint(AppDbContext dbContext, UserPlannerSettingsMapper mapper)
    : EndpointWithoutRequest<UserPlannerSettingsResponse>
{
    public override void Configure()
    {
        Get("/user/planner-settings");
        Roles(EndpointHelper.GetUserOrHigherRoles());
        Summary(s =>
        {
            s.Summary = "Get planner settings for the current user";
            s.Response<UserPlannerSettingsResponse>(200, "Success");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();

        var settings = await dbContext.UserPlannerSettings.FirstOrDefaultAsync(s => s.UserId == userId, ct);

        if (settings == null)
        {
            settings = new UserPlannerSettings { UserId = userId };
            await dbContext.UserPlannerSettings.AddAsync(settings, ct);
            await dbContext.SaveChangesAsync(ct);
        }

        await Send.OkAsync(mapper.ToResponse(settings), ct);
    }
}
