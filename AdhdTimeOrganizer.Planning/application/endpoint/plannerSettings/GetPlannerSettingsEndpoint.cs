using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using FastEndpoints;
using Npgsql;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.plannerSettings;

/// <summary>
/// Reads the caller's planner settings, creating the row on first read — the settings are not seeded, so a
/// user who has never opened the planner has no row and must still get their defaults.
/// <para>
/// That makes this a get-or-create, and <c>user_planner_settings.user_id</c> is unique, so two devices asking
/// for the first time at once cannot both insert: the loser takes a unique violation and used to answer 500
/// on a plain read. It re-reads the winner's row instead — the same shape
/// <c>CalendarProvisioner.EnsureForDateAsync</c> uses for <c>(user_id, date)</c> and
/// <c>BaseUserSettingsUpsertEndpoint</c> for the PUT half of this pair.
/// </para>
/// </summary>
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

            try
            {
                await dbContext.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // A concurrent first read got there first. Clearing the tracker rather than detaching just this
                // entity: the rejected insert would otherwise stay Added and be retried by any later
                // SaveChanges on this request, and nothing else on a read is worth keeping tracked.
                dbContext.ChangeTracker.Clear();
                settings = await dbContext.Set<UserPlannerSettings>().FirstAsync(s => s.UserId == userId, ct);
            }
        }

        await Send.OkAsync(UserPlannerSettingsResponse.FromEntity(settings), ct);
    }
}