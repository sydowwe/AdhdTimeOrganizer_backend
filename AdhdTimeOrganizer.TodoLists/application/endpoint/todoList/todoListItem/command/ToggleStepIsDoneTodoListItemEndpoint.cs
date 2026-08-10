using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FastEndpoints;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListItem.command;

public class ToggleStepIsDoneTodoListItemEndpoint(DbContext dbContext)
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

    protected override async Task PublishEvent(TodoListItem item, CancellationToken ct)
    {
        await new TodoListItemIsDoneChangedEvent(item.Id, item.IsDone)
            .PublishAsync(Mode.WaitForAll, ct);
    }
}