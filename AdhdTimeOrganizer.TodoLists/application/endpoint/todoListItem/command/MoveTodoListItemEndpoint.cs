using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using AdhdTimeOrganizer.TodoLists.application.endpoint.extensions;
using AdhdTimeOrganizer.TodoLists.application.validator;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.infrastructure.persistence.extensions;
using AdhdTimeOrganizer.TodoLists.infrastructure.settings;
using Humanizer;
using Microsoft.Extensions.Options;
using Sydowwe.Framework.application.endpoint.@base.command;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListItem.command;

public class MoveTodoListItemEndpoint(DbContext dbContext, IOptions<TodoListSettings> settings)
    : BasePatchEndpoint<TodoListItem, MoveToListTodoListItemRequest>(dbContext)
{
    private readonly TodoListSettings _settings = settings.Value;

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

    /// <summary>
    /// The IDOR guard <see cref="MoveToListTodoListItemValidator"/> cannot provide: it sees a number,
    /// and the FK is satisfied by whoever owns the destination list. 404 rather than 403, so a
    /// stranger's list id stays unconfirmable.
    /// </summary>
    protected override Task<bool> BeforeMapping(TodoListItem entity, MoveToListTodoListItemRequest req,
        CancellationToken ct = default) =>
        this.EnsureReferenceVisibleAsync<TodoList>(DbContext, nameof(req.DestinationListId), req.DestinationListId, ct);

    protected override void Mapping(TodoListItem entity, MoveToListTodoListItemRequest req)
    {
        entity.TodoListId = req.DestinationListId;
    }

    /// <summary>
    /// The item lands at the end of the destination list, not at whatever order it held in the list it
    /// came from — <c>DisplayOrder</c> is only meaningful within its group.
    /// </summary>
    protected override async Task<bool> AfterMapping(TodoListItem entity, MoveToListTodoListItemRequest req,
        CancellationToken ct = default)
    {
        entity.DisplayOrder = await DbContext.Set<TodoListItem>()
            .GetNextDisplayOrder(_settings, User.GetId(), e => e.TodoListId == req.DestinationListId, ct);
        return true;
    }
}
