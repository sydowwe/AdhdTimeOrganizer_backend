using AdhdTimeOrganizer.Core.application.endpoint.extensions;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
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
    /// The item's <c>ActivityId</c> is client-supplied and unchecked by everything else — see
    /// <see cref="ActivityIdOwnershipExtensions"/>. An item pointed at a stranger's activity is a task
    /// of the caller's whose activity every read loses to the global filter.
    /// </summary>
    protected override Task<bool> BeforeMapping(CreateTodoListItemRequest req, CancellationToken ct = default) =>
        this.EnsureOwnsActivityAsync(req, ownership, _dbContext, ct);

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