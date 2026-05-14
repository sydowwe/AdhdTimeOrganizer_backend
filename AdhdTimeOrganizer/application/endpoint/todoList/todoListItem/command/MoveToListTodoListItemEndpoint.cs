using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.infrastructure.persistence.extensions;
using AdhdTimeOrganizer.infrastructure.settings;
using FastEndpoints;
using Humanizer;
using Microsoft.Extensions.Options;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListItem.command;

public class MoveToListTodoListItemEndpoint(AppDbContext dbContext, IOptions<TodoListSettings> settings) : EndpointWithoutRequest
{
    private readonly TodoListSettings _settings = settings.Value;

    public override void Configure()
    {
        const string entityName = nameof(TodoListItem);
        Patch($"{entityName.Kebaberize()}/move-to-list/{{id:long:required}}/{{destinationListId:long:required}}");
        Summary(s =>
        {
            s.Summary = $"Moves {entityName} to a different todo list";
            s.Description = $"Moves {entityName} to a different todo list";
            s.Response(204, "Moved");
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

        var destinationListId = Route<long>("destinationListId");

        entity.TodoListId = destinationListId;
        entity.DisplayOrder = await dbContext.TodoListItems.GetNextDisplayOrder(_settings, User.GetId(), e => e.TodoListId == destinationListId, ct);

        dbContext.TodoListItems.Update(entity);
        await dbContext.SaveChangesAsync(ct);

        await Send.NoContentAsync(ct);
    }
}
