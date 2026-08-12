using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.plannerSettings;

public class UpdatePlannerSettingsEndpoint(DbContext dbContext)
    : Endpoint<UserPlannerSettingsRequest>
{
    public override void Configure()
    {
        Put("/planner/settings");

        Validator<UserPlannerSettingsValidator>();
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

        var settings = await dbContext.Set<UserPlannerSettings>().FirstOrDefaultAsync(s => s.UserId == userId, ct);

        if (settings == null)
        {
            settings = new UserPlannerSettings { UserId = userId };
            req.UpdateEntity(settings);
            await dbContext.Set<UserPlannerSettings>().AddAsync(settings, ct);
        }
        else
        {
            req.UpdateEntity(settings);
            dbContext.Set<UserPlannerSettings>().Update(settings);
        }

        await dbContext.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }
}