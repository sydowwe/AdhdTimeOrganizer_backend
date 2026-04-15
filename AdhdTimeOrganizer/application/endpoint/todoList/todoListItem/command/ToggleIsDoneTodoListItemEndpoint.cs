using AdhdTimeOrganizer.application.endpoint.todoList;
using AdhdTimeOrganizer.application.@event;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListItem.command;

public class ToggleIsDoneTodoListItemEndpoint(AppDbContext dbContext) : BaseToggleIsDoneTodoListEndpoint<TodoListItem>(dbContext)
{
    private readonly AppDbContext _dbContext = dbContext;

    protected override Task<List<TodoListItem>> FetchAndPrepare(ICollection<long> ids, DateTime now, CancellationToken ct) =>
        _dbContext.Set<TodoListItem>()
            .Where(e => ids.Contains(e.Id))
            .Include(e => e.Steps)
            .ToListAsync(ct);

    protected override async Task PublishEvent(TodoListItem entity, CancellationToken ct) =>
        await new TodoListItemIsDoneChangedEvent(entity.Id, entity.IsDone)
            .PublishAsync(Mode.WaitForAll, ct);
}
