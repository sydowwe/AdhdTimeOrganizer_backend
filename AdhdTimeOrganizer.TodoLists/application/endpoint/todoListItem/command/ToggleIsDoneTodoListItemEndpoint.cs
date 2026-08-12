using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FastEndpoints;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListItem.command;

public class ToggleIsDoneTodoListItemEndpoint(DbContext dbContext) : BaseToggleIsDoneTodoListEndpoint<TodoListItem>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;

    protected override Task<List<TodoListItem>> FetchAndPrepare(ICollection<long> ids, DateTime now, CancellationToken ct)
    {
        return _dbContext.Set<TodoListItem>()
            .Where(e => ids.Contains(e.Id))
            .Include(e => e.Steps)
            .ToListAsync(ct);
    }

    protected override async Task PublishEvent(TodoListItem entity, CancellationToken ct)
    {
        await new TodoListItemIsDoneChangedEvent(entity.Id, entity.IsDone)
            .PublishAsync(Mode.WaitForAll, ct);
    }
}