using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using AdhdTimeOrganizer.TodoLists.application.service;
using AdhdTimeOrganizer.TodoLists.application.validator;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.serviceContract;
using FastEndpoints;
using Microsoft.Extensions.Caching.Memory;
using Sydowwe.Framework.application.error;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.TodoLists.application.endpoint.todoList.todoListItem.command;

/// <summary>
/// <c>POST /todo-list-item/reschedule-overdue/undo</c> — takes a renegotiation back: each item goes back to its old
/// due date <b>and</b> its old slip record (<c>FirstDueDate</c>, <c>PostponeCount</c>), because the user is saying
/// the move did not happen. An undone renegotiation therefore never pushes an item toward the stuck-item offer.
/// <para>
/// Only items still open and still on the date the renegotiation gave them are restored (see
/// <see cref="RescheduleOverdueUndo"/>); anything ticked off or re-dated since stays as the user left it.
/// </para>
/// </summary>
public class UndoRescheduleOverdueTodoListItemEndpoint(
    DbContext dbContext,
    ITodoListItemDueReminderSync dueReminders,
    IMemoryCache undoStore)
    : Endpoint<UndoRescheduleOverdueTodoListItemRequest, UndoRescheduleOverdueTodoListItemResponse>
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Post("/todo-list-item/reschedule-overdue/undo");
        Roles(AllowedRoles());
        Validator<UndoRescheduleOverdueTodoListItemValidator>();
        Summary(s =>
        {
            s.Summary = "Take back a move of every overdue to-do item";
            s.Response<UndoRescheduleOverdueTodoListItemResponse>(200, "Restored — the ids of the items put back");
            s.Response(404, "The token is unknown, expired (15 minutes), already spent, or another user's");
            s.Response(409, "A concurrent write won the race. Nothing was written");
        });
    }

    public override async Task HandleAsync(UndoRescheduleOverdueTodoListItemRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        var snapshot = RescheduleOverdueUndo.Find(undoStore, req.UndoToken, userId);
        if (snapshot is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        try
        {
            var ids = snapshot.Items.Select(i => i.Id).ToList();
            var items = await dbContext.Set<TodoListItem>()
                .Where(i => i.UserId == userId && ids.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, ct);

            var restored = new List<long>();
            foreach (var before in snapshot.Items)
            {
                if (!items.TryGetValue(before.Id, out var item) || item.IsDone || item.DueDate != snapshot.DueDateAfter)
                    continue;

                item.DueDate = before.DueDate;
                item.FirstDueDate = before.FirstDueDate;
                item.PostponeCount = before.PostponeCount;
                restored.Add(item.Id);
            }

            await dbContext.SaveChangesAsync(ct);
            RescheduleOverdueUndo.Forget(undoStore, req.UndoToken);

            // Back on its old, passed date, a timed item's reminder is withdrawn again.
            if (restored.Count > 0)
                await dueReminders.SyncAsync(restored, ct);

            await Send.OkAsync(new UndoRescheduleOverdueTodoListItemResponse { RestoredIds = restored }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(this.ErrorStatus(result.ErrorType), ct);
        }
    }
}
