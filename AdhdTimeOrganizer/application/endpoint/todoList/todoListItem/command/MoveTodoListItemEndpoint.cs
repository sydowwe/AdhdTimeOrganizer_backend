using AdhdTimeOrganizer.application.dto.request.todoList;
using AdhdTimeOrganizer.application.dto.response.todoList;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.infrastructure.persistence.extensions;
using AdhdTimeOrganizer.infrastructure.settings;
using Humanizer;
using Microsoft.Extensions.Options;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListItem.command;

public class MoveTodoListItemEndpoint(AppDbContext dbContext, IOptions<TodoListSettings> settings)
    : BasePatchEndpoint<TodoListItem, MoveToListTodoListItemRequest, TodoListItemResponse>(dbContext)
{
    private readonly TodoListSettings _settings = settings.Value;
    private readonly AppDbContext _dbContext = dbContext;

    public override void Configure()
    {
        const string entityName = nameof(TodoListItem);
        Patch($"/{entityName.Kebaberize()}/{{id}}/move");
        Roles(AllowedRoles());
        Summary(s =>
        {
            s.Summary = $"Move {entityName} to a different todo list";
            s.Description = $"Moves {entityName} to a different todo list";
            s.Response(204, "Moved");
            s.Response(404, "Not found");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(MoveToListTodoListItemRequest req, CancellationToken ct)
    {
        var entity = await _dbContext.TodoListItems.FindAsync([Route<long>("id")], ct);
        if (entity == null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var destinationListId = req.DestinationListId;
        entity.TodoListId = destinationListId;
        entity.DisplayOrder = await _dbContext.TodoListItems.GetNextDisplayOrder(
            _settings, User.GetId(), e => e.TodoListId == destinationListId, ct);

        _dbContext.TodoListItems.Update(entity);
        await _dbContext.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }

    protected override void Mapping(TodoListItem entity, MoveToListTodoListItemRequest req) { }
}
