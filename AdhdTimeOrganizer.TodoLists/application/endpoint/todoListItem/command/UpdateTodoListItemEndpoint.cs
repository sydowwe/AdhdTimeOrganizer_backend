using AdhdTimeOrganizer.Core.application.endpoint.extensions;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using AdhdTimeOrganizer.TodoLists.application.endpoint.extensions;
using AdhdTimeOrganizer.TodoLists.application.service;
using AdhdTimeOrganizer.TodoLists.application.validator;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.application.endpoint.@base.command;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListItem.command;

public class UpdateTodoListItemEndpoint(
    DbContext dbContext,
    IReferencedIdOwnershipService ownership,
    IUserTimeZoneResolver timeZones)
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

    /// <summary>
    /// Everything the PUT owns but does not take from the body, in the one order that works: the steps have
    /// to be in their final shape before the completion state can be snapped across them.
    /// </summary>
    protected override async Task<bool> AfterMapping(TodoListItem entity, UpdateTodoListItemRequest req, CancellationToken ct = default)
    {
        // Read off the change tracker before SetDone below can touch anything: the values as loaded.
        var entry = DbContext.Entry(entity);
        var dueDateBefore = entry.Property(e => e.DueDate).OriginalValue;
        var wasDone = entry.Property(e => e.IsDone).OriginalValue;

        if (req.Steps is not null)
            entity.ReconcileStepsFrom(req.Steps);

        // TotalCount is client-owned, DoneCount is not (see BaseUpdateTodoListRequest), so the count is
        // re-derived rather than taken from the body: an item that becomes counted starts at zero, and
        // lowering the total must not leave a count above it — CK_TodoListItem_DoneCount_LessOrEqual_TotalCount
        // would refuse the save.
        if (entity.TotalCount is { } totalCount)
            entity.DoneCount = Math.Min(entity.DoneCount ?? 0, totalCount);

        // Only on a flip. SetDone is a snap, not a merge — it rewrites DoneCount and every step's IsDone —
        // so calling it on an edit that did not touch completion would silently untick a half-finished
        // checklist every time the user changed the note. The flip itself matches what the toggle endpoint
        // does with no forced value: done -> not done clears the count and the steps, and back again fills them.
        if (entity.IsDone != req.IsDone)
            entity.SetDone(req.IsDone);

        // FO3: pushing an overdue due date later is a postponement. The zone is read only when the date moved
        // later at all, so an ordinary edit costs nothing extra. An edit that also ticks the item off is not one.
        if (entity.DueDate > dueDateBefore)
        {
            var today = DateOnly.FromDateTime(WallClockZone.FromUtc(DateTime.UtcNow, await timeZones.GetAsync(User.GetId(), ct)));
            TodoListItemPostponeRule.Apply(entity, wasDone || entity.IsDone, dueDateBefore, entity.DueDate, today);
        }

        return true;
    }
}