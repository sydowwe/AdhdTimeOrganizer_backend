using AdhdTimeOrganizer.Core.domain.serviceContract;
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
/// <c>POST /todo-list-item/reschedule-overdue</c> — the renegotiation banner's "move them all": every open item
/// whose due date is before the user's today gets today + <c>Days</c>, in one save.
/// <para>
/// It replaces a PUT per item from the client (FO3 phase 2). That loop could half-fail, sent each item's whole body
/// back to change one field, and decided "overdue" on the browser's clock. Here the set is chosen on the user's
/// own date, and every move goes through <see cref="TodoListItemPostponeRule"/> — always a postponement, since
/// each item's due date has passed and the new one is today or later.
/// </para>
/// <para>
/// The list id is optional so a later "coming back" screen (F1-04) can clear every list at once. A list id that
/// is not the caller's moves nothing, exactly like an empty list of their own.
/// </para>
/// <para>
/// The response's <c>UndoToken</c> takes it back through <see cref="UndoRescheduleOverdueTodoListItemEndpoint"/>,
/// postponements included (<see cref="RescheduleOverdueUndo"/>).
/// </para>
/// </summary>
public class RescheduleOverdueTodoListItemEndpoint(
    DbContext dbContext,
    IUserTimeZoneResolver timeZones,
    ITodoListItemDueReminderSync dueReminders,
    IMemoryCache undoStore)
    : Endpoint<RescheduleOverdueTodoListItemRequest, RescheduleOverdueTodoListItemResponse>
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Post("/todo-list-item/reschedule-overdue");
        Roles(AllowedRoles());
        Validator<RescheduleOverdueTodoListItemValidator>();
        Summary(s =>
        {
            s.Summary = "Move every overdue to-do item to today or a later day";
            s.Description = "Sets DueDate = the user's today + Days on every open item of the list (or of every list "
                            + "when ListId is null) whose due date has passed. Each move is recorded as a postponement.";
            s.Response<RescheduleOverdueTodoListItemResponse>(200, "The new due date and the ids that moved");
            s.Response(400, "Days outside 0–365, or a non-positive ListId");
            s.Response(409, "Concurrent write to one of the items lost the row_version race");
        });
    }

    public override async Task HandleAsync(RescheduleOverdueTodoListItemRequest req, CancellationToken ct)
    {
        try
        {
            var userId = User.GetId();
            var today = DateOnly.FromDateTime(WallClockZone.FromUtc(DateTime.UtcNow, await timeZones.GetAsync(userId, ct)));
            var dueDate = today.AddDays(req.Days);

            // UserId by hand as well as through the host's global filter: this slice compiles against a plain
            // DbContext, and the id is in hand for the zone anyway. Served by the partial (UserId, IsDone, DueDate)
            // index the dashboard widget already uses.
            var items = await dbContext.Set<TodoListItem>()
                .Where(i => i.UserId == userId
                            && !i.IsDone
                            && i.DueDate != null
                            && i.DueDate < today
                            && (req.ListId == null || i.TodoListId == req.ListId))
                .ToListAsync(ct);

            // The before-image for the undo, taken before the rule writes the slip record.
            var before = items
                .Select(i => new RescheduleOverdueUndo.ItemBefore(i.Id, i.DueDate, i.FirstDueDate, i.PostponeCount))
                .ToList();

            foreach (var item in items)
            {
                var from = item.DueDate;
                item.DueDate = dueDate;
                TodoListItemPostponeRule.Apply(item, wasDone: false, from, dueDate, today);
            }

            await dbContext.SaveChangesAsync(ct);

            var undoToken = items.Count == 0
                ? null
                : RescheduleOverdueUndo.Store(undoStore, new RescheduleOverdueUndo.Snapshot(userId, dueDate, before));

            // Every moved item, not the first: a timed item lands on its new date with its time of day kept, so
            // its reminder moves with it. One batched call after the commit.
            await dueReminders.SyncAsync(items.Select(i => i.Id).ToList(), ct);

            await Send.OkAsync(new RescheduleOverdueTodoListItemResponse
            {
                DueDate = dueDate,
                MovedIds = items.Select(i => i.Id).ToList(),
                UndoToken = undoToken
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(this.ErrorStatus(result.ErrorType), ct);
        }
    }
}
