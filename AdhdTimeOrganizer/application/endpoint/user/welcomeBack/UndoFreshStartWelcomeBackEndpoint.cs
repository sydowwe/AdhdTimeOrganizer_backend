using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.application.service.welcomeBack;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Sydowwe.Framework.application.error;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.user.welcomeBack;

/// <summary>
/// <c>POST /welcome-back/fresh-start/undo</c> — FO5: takes a fresh start back and reopens the card, so the user can
/// choose again. Each row is restored only if it is still exactly as the fresh start left it (see
/// <see cref="FreshStartUndo"/>); everything else the user has done since stands.
/// <para>
/// The restore puts back the slip record as well as the date: a spread to-do's postponement and a carried block's
/// carry-over are taken back with the move, because the user is saying the move did not happen.
/// </para>
/// </summary>
public class UndoFreshStartWelcomeBackEndpoint(
    AppDbContext dbContext,
    IReminderRegistrationService reminders,
    IMemoryCache undoStore)
    : Endpoint<UndoFreshStartRequest, UndoFreshStartResponse>
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Post("/welcome-back/fresh-start/undo");
        Roles(AllowedRoles());
        Validator<UndoFreshStartValidator>();
        Summary(s =>
        {
            s.Summary = "Take back a fresh start and reopen the welcome-back card";
            s.Response<UndoFreshStartResponse>(200, "Restored — the ids of what was put back");
            s.Response(404, "The token is unknown, expired (15 minutes), already spent, or another user's");
            s.Response(409, "A concurrent write won the race. Nothing was written");
        });
    }

    public override async Task HandleAsync(UndoFreshStartRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        var snapshot = FreshStartUndo.Find(undoStore, req.UndoToken, userId);
        if (snapshot is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        try
        {
            var todoIds = snapshot.Todos.Select(t => t.Id).ToList();
            var todos = await dbContext.TodoListItems
                .Where(i => i.UserId == userId && todoIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, ct);

            var restoredTodos = new List<long>();
            foreach (var before in snapshot.Todos)
            {
                if (!todos.TryGetValue(before.Id, out var item) || item.IsDone || item.DueDate != before.DueDateAfter)
                    continue;

                item.DueDate = before.DueDate;
                item.DueTime = before.DueTime;
                item.FirstDueDate = before.FirstDueDate;
                item.PostponeCount = before.PostponeCount;
                restoredTodos.Add(item.Id);
            }

            var taskIds = snapshot.Tasks.Select(t => t.Id).ToList();
            var tasks = await dbContext.PlannerTasks
                .Where(t => t.UserId == userId && taskIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, ct);

            var restoredTasks = new List<long>();
            foreach (var before in snapshot.Tasks)
            {
                if (!tasks.TryGetValue(before.Id, out var task)
                    || task.CalendarId != before.CalendarIdAfter
                    || task.Status != before.StatusAfter)
                    continue;

                task.CalendarId = before.CalendarId;
                task.Status = before.Status;
                task.SkipReason = before.SkipReason;
                task.SkipReasonKey = before.SkipReasonKey;
                task.ActualStartTime = before.ActualStartTime;
                task.ActualEndTime = before.ActualEndTime;
                task.OriginalDate = before.OriginalDate;
                task.OriginalStartTime = before.OriginalStartTime;
                task.CarryOverCount = before.CarryOverCount;
                restoredTasks.Add(task.Id);
            }

            var notifications = await dbContext.Notifications
                .Where(n => n.UserId == userId && n.IsRead && snapshot.NotificationIds.Contains(n.Id))
                .ToListAsync(ct);
            foreach (var notification in notifications)
            {
                notification.IsRead = false;
                notification.ReadAt = null;
            }

            var user = await dbContext.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId, ct);
            user.WelcomeBackAwayFrom = snapshot.AwayFrom;
            user.WelcomeBackReturnedAt = snapshot.ReturnedAt;

            await dbContext.SaveChangesAsync(ct);
            FreshStartUndo.Forget(undoStore, req.UndoToken);

            if (restoredTasks.Count > 0)
                await reminders.SyncForPlannerTasksAsync(restoredTasks, ct);

            await Send.OkAsync(new UndoFreshStartResponse
            {
                RestoredTodoIds = restoredTodos,
                RestoredTaskIds = restoredTasks,
                NotificationsRestored = notifications.Count
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
