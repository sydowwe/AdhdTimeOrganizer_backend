using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using AdhdTimeOrganizer.TodoLists.application.validator;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Humanizer;
using Microsoft.Extensions.Options;
using Sydowwe.Framework.application.endpoint.@base.command;
using Sydowwe.Framework.application.extensions;
using AdhdTimeOrganizer.TodoLists.infrastructure.persistence.extensions;
using AdhdTimeOrganizer.TodoLists.infrastructure.settings;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListItem.command;

public class MoveTodoListItemEndpoint(DbContext dbContext, IOptions<TodoListSettings> settings)
    : BasePatchEndpoint<TodoListItem, MoveToListTodoListItemRequest>(dbContext)
{
    private readonly TodoListSettings _settings = settings.Value;
    private readonly DbContext _dbContext = dbContext;

    public override void Configure()
    {
        const string entityName = nameof(TodoListItem);
        Patch($"/{entityName.Kebaberize()}/{{id:long:required}}/move");
        Validator<MoveToListTodoListItemValidator>();

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
        var entity = await _dbContext.Set<TodoListItem>().FindAsync([Route<long>("id")], ct);
        if (entity == null)
        {
            AddError("TodoListItem not found.");
            await Send.ErrorsAsync(404, ct);
            return;
        }

        var destinationListId = req.DestinationListId;
        entity.TodoListId = destinationListId;
        entity.DisplayOrder = await _dbContext.Set<TodoListItem>().GetNextDisplayOrder(
            _settings, User.GetId(), e => e.TodoListId == destinationListId, ct);

        _dbContext.Set<TodoListItem>().Update(entity);
        await _dbContext.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }

    protected override void Mapping(TodoListItem entity, MoveToListTodoListItemRequest req)
    {
    }
}