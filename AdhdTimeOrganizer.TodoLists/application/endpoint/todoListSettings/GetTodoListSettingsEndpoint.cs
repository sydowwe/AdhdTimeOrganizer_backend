using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListSettings;

/// <summary>
/// Reads the caller's to-do settings, or the defaults if they have no row yet. Does not insert on read — same
/// reasoning as <c>GetRoutineSettingsEndpoint</c>: a row created by a read holds nothing but defaults.
/// </summary>
public class GetTodoListSettingsEndpoint(DbContext dbContext)
    : EndpointWithoutRequest<UserTodoListSettingsResponse>
{
    public override void Configure()
    {
        Get("/todo-list/settings");

        Summary(s =>
        {
            s.Summary = "Get to-do settings for the current user";
            s.Response<UserTodoListSettingsResponse>(200, "Success");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();

        var settings = await dbContext.Set<UserTodoListSettings>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, ct);

        await Send.OkAsync(UserTodoListSettingsResponse.FromEntity(settings), ct);
    }
}
