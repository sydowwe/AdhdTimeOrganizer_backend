using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Humanizer;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListItem.command;

public class ChangePriorityTodoListItemEndpoint(AppDbContext dbContext) : EndpointWithoutRequest
{

    public override void Configure()
    {
        const string entityName = nameof(TodoListItem);
        Patch($"{entityName.Kebaberize()}/change-priority/{{id:long:required}}/{{priorityId:long:required}}");
        Summary(s =>
        {
            s.Summary = $"Toggles {entityName} IsDone status";
            s.Description = $"Toggles {entityName} IsDone status";
            s.Response(204, "Toggled");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var entity = await dbContext.TodoListItems.FindAsync([Route<long>("id")], ct);

        if (entity == null)
        {
            AddError("Entity not found");
            await Send.NotFoundAsync(ct);
            return;
        }

        entity.TaskPriorityId = Route<long>("priorityId");

        dbContext.TodoListItems.Update(entity);
        await dbContext.SaveChangesAsync(ct);

        await Send.NoContentAsync(ct);
    }
}
