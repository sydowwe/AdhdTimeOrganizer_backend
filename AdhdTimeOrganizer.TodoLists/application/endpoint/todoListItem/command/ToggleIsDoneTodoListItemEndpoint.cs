using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.serviceContract;
using FastEndpoints;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListItem.command;

public class ToggleIsDoneTodoListItemEndpoint(DbContext dbContext, ITodoListItemDueReminderSync dueReminders)
    : BaseToggleIsDoneTodoListEndpoint<TodoListItem>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;
    private List<long> _toggledIds = [];

    protected override async Task<List<TodoListItem>> FetchAndPrepare(ICollection<long> ids, DateTime now, CancellationToken ct)
    {
        var items = await _dbContext.Set<TodoListItem>()
            .Where(e => ids.Contains(e.Id))
            .Include(e => e.Steps)
            .ToListAsync(ct);

        _toggledIds = items.Select(i => i.Id).ToList();
        return items;
    }

    /// <summary>
    /// Both directions: ticking an item off withdraws its due reminder, un-ticking one whose due time is still
    /// ahead brings it back. After the commit, once for the whole batch.
    /// </summary>
    protected override Task AfterSave(CancellationToken ct) => dueReminders.SyncAsync(_toggledIds, ct);

    protected override async Task PublishEvent(TodoListItem entity, CancellationToken ct)
    {
        await new TodoListItemIsDoneChangedEvent(entity.Id, entity.UserId, entity.IsDone)
            .PublishAsync(Mode.WaitForAll, ct);
    }
}