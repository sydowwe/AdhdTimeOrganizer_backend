using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.serviceContract;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListItem.command;

public class DeleteTodoListItemEndpoint(DbContext dbContext, ITodoListItemDueReminderSync dueReminders)
    : BaseDeleteEndpoint<TodoListItem>(dbContext)
{
    /// <summary>The item's due reminder goes with it, or it would sit on the user's upcoming list until its instant.</summary>
    protected override Task AfterSave(TodoListItem entity, CancellationToken ct = default) =>
        dueReminders.CancelAsync([entity.Id], ct);
}
