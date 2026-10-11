using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.serviceContract;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoList.command;

/// <summary>
/// Deleting a list cascades its items away in the database (<c>TodoListConfiguration</c>), so none of them passes
/// through a path that would withdraw its due reminder. Their ids are read before the delete, while they still
/// exist, and the reminders are cancelled once the delete has committed.
/// </summary>
public class DeleteTodoListEndpoint(DbContext dbContext, ITodoListItemDueReminderSync dueReminders)
    : BaseDeleteEndpoint<TodoList>(dbContext)
{
    private readonly DbContext _dbContext = dbContext;
    private List<long> _cascadedItemIds = [];

    protected override async Task BeforeDeleteAsync(TodoList entity, CancellationToken ct = default) =>
        _cascadedItemIds = await _dbContext.Set<TodoListItem>()
            .Where(i => i.TodoListId == entity.Id)
            .Select(i => i.Id)
            .ToListAsync(ct);

    protected override Task AfterSave(TodoList entity, CancellationToken ct = default) =>
        dueReminders.CancelAsync(_cascadedItemIds, ct);
}
