using AdhdTimeOrganizer.Routines.application.dto.response.todoList;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Routines.application.endpoint.todoList.routineSettings;

/// <summary>
/// Reads the caller's routine settings, or the defaults if they have no row yet.
/// <para>
/// Unlike <c>GetPlannerSettingsEndpoint</c> this does **not** insert a row on read. Every field here defaults
/// to null, so a row created by a read would hold nothing — and this endpoint is hit on every visit to the
/// routine views, which would make a write out of the most common read in the module.
/// </para>
/// </summary>
public class GetRoutineSettingsEndpoint(DbContext dbContext)
    : EndpointWithoutRequest<UserRoutineSettingsResponse>
{
    public override void Configure()
    {
        Get("/routine/settings");

        Summary(s =>
        {
            s.Summary = "Get routine settings for the current user";
            s.Response<UserRoutineSettingsResponse>(200, "Success");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();

        var settings = await dbContext.Set<UserRoutineSettings>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, ct);

        await Send.OkAsync(UserRoutineSettingsResponse.FromEntity(settings), ct);
    }
}
