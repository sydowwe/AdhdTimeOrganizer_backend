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

public class ChangePriorityTodoListItemEndpoint(DbContext dbContext, IOptions<TodoListSettings> settings)
    : BasePatchEndpoint<TodoListItem, ChangePriorityTodoListItemRequest>(dbContext)
{
    private readonly TodoListSettings _settings = settings.Value;

    public override void Configure()
    {
        const string entityName = nameof(TodoListItem);
        Patch($"/{entityName.Kebaberize()}/{{id:long:required}}/priority");
        Validator<ChangePriorityTodoListItemValidator>();

        Summary(s =>
        {
            s.Summary = $"Change priority of {entityName}";
            s.Description = $"Changes the priority of an existing {entityName}";
            s.Response(204, "Success");
            s.Response(404, "Not found");
            s.Response(400, "Bad request");
        });
    }

    /// <summary>
    /// <see cref="ChangePriorityTodoListItemValidator"/> checks the id's magnitude only, and another
    /// user's rank satisfies the FK — so this is the ownership check. 404, never 403, to keep the id
    /// unconfirmable across accounts, the same way <see cref="MoveTodoListItemEndpoint"/> does it.
    /// </summary>
    protected override Task<bool> BeforeMapping(TodoListItem entity, ChangePriorityTodoListItemRequest req,
        CancellationToken ct = default) =>
        this.EnsureReferenceVisibleAsync<TaskPriority>(DbContext, nameof(req.PriorityId), req.PriorityId, ct);

    protected override void Mapping(TodoListItem entity, ChangePriorityTodoListItemRequest req)
    {
        entity.TaskPriorityId = req.PriorityId;
    }

    /// <summary>
    /// <c>DisplayOrder</c> is scoped <em>by</em> <c>TaskPriorityId</c> — see
    /// <c>ChangeDisplayOrderTodoListItemEndpoint.GroupFilterExpression</c> — so the two columns move
    /// together. Left alone, the item would keep an order minted for the group it just left and land
    /// somewhere in the middle of the new one.
    /// </summary>
    protected override async Task<bool> AfterMapping(TodoListItem entity, ChangePriorityTodoListItemRequest req,
        CancellationToken ct = default)
    {
        entity.DisplayOrder = await DbContext.Set<TodoListItem>()
            .GetNextDisplayOrder(_settings, User.GetId(), req.PriorityId, ct);
        return true;
    }
}
