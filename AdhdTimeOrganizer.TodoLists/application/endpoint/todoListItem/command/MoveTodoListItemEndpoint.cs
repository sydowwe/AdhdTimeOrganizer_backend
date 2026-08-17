using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using AdhdTimeOrganizer.TodoLists.application.validator;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.infrastructure.persistence.extensions;
using AdhdTimeOrganizer.TodoLists.infrastructure.settings;
using Humanizer;
using Microsoft.Extensions.Options;
using Sydowwe.Framework.application.endpoint.@base.command;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

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
        try
        {
            var entity = await _dbContext.Set<TodoListItem>().FindAsync([Route<long>("id")], ct);
            if (entity == null)
            {
                AddError("TodoListItem not found.");
                await Send.ErrorsAsync(404, ct);
                return;
            }

            var destinationListId = req.DestinationListId;

            // TodoList is IEntityWithUser, so AppDbContext's global query filter already scopes this to the
            // caller -- a destination list belonging to another user simply doesn't exist from here, so this
            // doubles as the IDOR guard MoveToListTodoListItemValidator doesn't provide.
            var destinationListExists = await _dbContext.Set<TodoList>().AnyAsync(l => l.Id == destinationListId, ct);
            if (!destinationListExists)
            {
                AddError("Destination TodoList not found.");
                await Send.ErrorsAsync(404, ct);
                return;
            }

            entity.TodoListId = destinationListId;
            entity.DisplayOrder = await _dbContext.Set<TodoListItem>().GetNextDisplayOrder(
                _settings, User.GetId(), e => e.TodoListId == destinationListId, ct);

            _dbContext.Set<TodoListItem>().Update(entity);
            await _dbContext.SaveChangesAsync(ct);
            await Send.NoContentAsync(ct);
        }
        catch (Exception ex)
        {
            // Mirrors BasePatchEndpoint.HandleAsync's mapping (bypassed here because this endpoint overrides
            // HandleAsync directly) so a unique-index collision on (UserId, ActivityId, TodoListId) reports the
            // same clean 409 every other reorder/update path in this API gives, instead of an unmapped 500.
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(EndpointHelper.ToStatusCode(result.ErrorType), ct);
        }
    }

    protected override void Mapping(TodoListItem entity, MoveToListTodoListItemRequest req)
    {
    }
}