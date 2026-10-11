using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.serviceContract;
using FastEndpoints;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListItem.command;

public class ToggleStepIsDoneTodoListItemEndpoint(DbContext dbContext, ITodoListItemDueReminderSync dueReminders)
    : BaseToggleStepIsDoneEndpoint<TodoListItem>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;

    protected override async Task<TodoListItem?> FetchItem(long itemId, CancellationToken ct)
    {
        return await _dbContext.Set<TodoListItem>()
            .Where(e => e.Id == itemId)
            .Include(e => e.Steps)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Runs only past the base's save-failure gate, so it doubles as the post-commit hook: ticking the last step
    /// completes the item and withdraws its due reminder, un-ticking a step of a finished item brings it back.
    /// </summary>
    protected override async Task PublishEvent(TodoListItem item, CancellationToken ct)
    {
        await new TodoListItemIsDoneChangedEvent(item.Id, item.UserId, item.IsDone)
            .PublishAsync(Mode.WaitForAll, ct);

        await dueReminders.SyncAsync([item.Id], ct);
    }
}