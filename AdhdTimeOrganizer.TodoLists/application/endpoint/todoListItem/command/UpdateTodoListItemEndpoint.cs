using AdhdTimeOrganizer.Core.application.endpoint.extensions;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using AdhdTimeOrganizer.TodoLists.application.endpoint.extensions;
using AdhdTimeOrganizer.TodoLists.application.validator;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListItem.command;

public class UpdateTodoListItemEndpoint(DbContext dbContext, IReferencedIdOwnershipService ownership)
    : BaseUpdateEndpoint<TodoListItem, UpdateTodoListItemRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<UpdateTodoListItemValidator>();
    }

    /// <summary>
    /// The edit rewrites <c>ActivityId</c>, <c>TaskPriorityId</c> and <c>PairedLeisureActivityId</c>, so
    /// it can repoint one of the caller's own items at any of three rows belonging to someone else —
    /// without creating anything, which is what makes it the more dangerous of the two write paths.
    /// Same guards as the create path, minus <c>TodoListId</c>: the PUT does not carry one, because
    /// changing lists is <c>MoveTodoListItemEndpoint</c>'s job.
    /// </summary>
    protected override async Task<bool> BeforeMapping(UpdateTodoListItemRequest req, CancellationToken ct = default)
    {
        if (!await this.EnsureOwnsActivityAsync(req, ownership, DbContext, ct))
            return false;

        return await this.EnsureReferenceVisibleAsync<TaskPriority>(
                   DbContext, nameof(req.TaskPriorityId), req.TaskPriorityId, ct)
               && await this.EnsureActivityVisibleAsync(
                   DbContext, nameof(req.PairedLeisureActivityId), req.PairedLeisureActivityId, ct);
    }

    protected override Task<bool> AfterMapping(TodoListItem entity, UpdateTodoListItemRequest req, CancellationToken ct = default)
    {
        if (req.Steps is not null)
            entity.Steps = req.Steps.Select(s => new TodoListStep
            {
                Name = s.Name,
                Order = s.Order,
                Note = s.Note,
                IsDone = s.Id.HasValue && entity.Steps.FirstOrDefault(e => e.Id == s.Id.Value)?.IsDone == true
            }).ToList();

        if (req is { TotalCount: not null, DoneCount: null })
            entity.DoneCount = 0;

        return Task.FromResult(true);
    }
}