using AdhdTimeOrganizer.application.endpoint.todoList;
using AdhdTimeOrganizer.application.@event;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.todoList.todoListItem.command;

public class ToggleStepIsDoneTodoListItemEndpoint(AppDbContext dbContext)
    : BaseToggleStepIsDoneEndpoint<TodoListItem>(dbContext)
{
    private readonly AppDbContext _dbContext = dbContext;

    protected override async Task<TodoListItem?> FetchItem(long itemId, CancellationToken ct) =>
        await _dbContext.Set<TodoListItem>()
            .Where(e => e.Id == itemId)
            .Include(e => e.Steps)
            .FirstOrDefaultAsync(ct);

    protected override async Task PublishEvent(TodoListItem item, CancellationToken ct) =>
        await new TodoListItemIsDoneChangedEvent(item.Id, item.IsDone)
            .PublishAsync(Mode.WaitForAll, ct);
}
