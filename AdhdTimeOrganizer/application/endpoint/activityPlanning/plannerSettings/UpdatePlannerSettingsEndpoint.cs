using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.mapper.user;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.plannerSettings;

public class UpdatePlannerSettingsEndpoint(AppDbContext dbContext, UserPlannerSettingsMapper mapper)
    : Endpoint<UserPlannerSettingsRequest>
{
    public override void Configure()
    {
        Put("/planner/settings");
        Roles(EndpointHelper.GetUserOrHigherRoles());
        Summary(s =>
        {
            s.Summary = "Update planner settings for the current user";
            s.Response(204, "Success");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(UserPlannerSettingsRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var settings = await dbContext.UserPlannerSettings.FirstOrDefaultAsync(s => s.UserId == userId, ct);

        if (settings == null)
        {
            settings = new UserPlannerSettings { UserId = userId };
            mapper.UpdateEntity(req, settings);
            await dbContext.UserPlannerSettings.AddAsync(settings, ct);
        }
        else
        {
            mapper.UpdateEntity(req, settings);
            dbContext.UserPlannerSettings.Update(settings);
        }

        await dbContext.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }
}
