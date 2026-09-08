using AdhdTimeOrganizer.Core.application.endpoint.extensions;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Core.infrastructure.persistence.extensions;
using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using AdhdTimeOrganizer.TodoLists.application.endpoint.extensions;
using AdhdTimeOrganizer.TodoLists.application.validator;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.infrastructure.persistence.extensions;
using AdhdTimeOrganizer.TodoLists.infrastructure.settings;
using Microsoft.Extensions.Options;
using Sydowwe.Framework.application.endpoint.@base.command;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListItem.command;

public class CreateTodoListItemEndpoint(
    DbContext dbContext,
    IOptions<TodoListSettings> settings,
    IReferencedIdOwnershipService ownership)
    : BaseCreateEndpoint<TodoListItem, CreateTodoListItemRequest>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;
    private readonly TodoListSettings _settings = settings.Value;

    public override void Configure()
    {
        base.Configure();
        Validator<CreateTodoListItemValidator>();
    }

    /// <summary>
    /// Every id on this request is client-supplied and unchecked by everything else — the validator
    /// sees magnitudes, and each FK is satisfied by whoever owns the target row. An item pointed at a
    /// stranger's activity, list or rank is a task of the caller's whose other end every read loses to
    /// the global filter: <c>TodoListItemResponse.Projection</c> reaches through the <em>required</em>
    /// <c>TaskPriority</c> navigation, so the item comes back mangled or not at all.
    /// </summary>
    /// <remarks>
    /// 404 on all of them, never 403 — see <see cref="ActivityIdOwnershipExtensions"/> and
    /// <see cref="CallerVisibleReferenceExtensions"/> for why the two cases have to be indistinguishable.
    /// </remarks>
    protected override async Task<bool> BeforeMapping(CreateTodoListItemRequest req, CancellationToken ct = default)
    {
        if (!await this.EnsureOwnsActivityAsync(req, ownership, _dbContext, ct))
            return false;

        return await this.EnsureReferenceVisibleAsync<TodoList>(
                   _dbContext, nameof(req.TodoListId), req.TodoListId, ct)
               && await this.EnsureReferenceVisibleAsync<TaskPriority>(
                   _dbContext, nameof(req.TaskPriorityId), req.TaskPriorityId, ct)
               && await this.EnsureActivityVisibleAsync(
                   _dbContext, nameof(req.PairedLeisureActivityId), req.PairedLeisureActivityId, ct);
    }

    protected override async Task<bool> AfterMapping(TodoListItem entity, CreateTodoListItemRequest req, CancellationToken ct = default)
    {
        entity.DisplayOrder = await _dbContext.Set<TodoListItem>().GetNextDisplayOrder(_settings, User.GetId(), entity.TaskPriorityId, ct);
        entity.TodoListId = req.TodoListId;

        if (req.Steps is { Count: > 0 })
        {
            entity.Steps = req.Steps.Select(s => new TodoListStep { Name = s.Name, Order = s.Order, Note = s.Note }).ToList();
            entity.DoneCount = 0;
        }
        else if (req.TotalCount.HasValue)
        {
            entity.DoneCount = 0;
        }

        return true;
    }
}