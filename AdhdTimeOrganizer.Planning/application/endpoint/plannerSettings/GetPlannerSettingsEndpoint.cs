using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.plannerSettings;

public class GetPlannerSettingsEndpoint(DbContext dbContext)
    : EndpointWithoutRequest<UserPlannerSettingsResponse>
{
    public override void Configure()
    {
        Get("/planner/settings");

        Summary(s =>
        {
            s.Summary = "Get planner settings for the current user";
            s.Response<UserPlannerSettingsResponse>(200, "Success");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();

        var settings = await dbContext.Set<UserPlannerSettings>().FirstOrDefaultAsync(s => s.UserId == userId, ct);

        if (settings == null)
        {
            settings = new UserPlannerSettings { UserId = userId };
            await dbContext.Set<UserPlannerSettings>().AddAsync(settings, ct);
            await dbContext.SaveChangesAsync(ct);
        }

        await Send.OkAsync(UserPlannerSettingsResponse.FromEntity(settings), ct);
    }
}