using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.application.service.welcomeBack;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using AdhdTimeOrganizer.TodoLists.application.service;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.serviceContract;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Sydowwe.Framework.application.error;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.domain.result;
using Sydowwe.Framework.infrastructure.persistence;
using Sydowwe.Notifications.domain.entity;

namespace AdhdTimeOrganizer.application.endpoint.user.welcomeBack;

/// <summary>
/// <c>POST /welcome-back/fresh-start</c> — FO5: answers the card. The overdue to-dos, the blocks left open since the
/// absence began and the unread notifications are each dealt with as the request says, in <b>one save</b>, and the
/// card closes with them. Returns an undo token (<see cref="FreshStartUndo"/>).
/// <para>
/// The rows are the ones <see cref="WelcomeBackPile"/> names, read again here rather than taken from the client:
/// the card may be minutes old, and an item the user ticked off since is no longer in the pile. A request with no
/// card open is a 409 with nothing written, which also makes a double submit harmless.
/// </para>
/// <para>
/// Each change goes through the rule its slice already keeps:
/// <list type="bullet">
/// <item>a spread to-do is a postponement under <see cref="TodoListItemPostponeRule"/>, exactly as the overdue
/// banner's "move them all" is; a cleared date ("someday") is not, by that same rule;</item>
/// <item>a carried block gets <c>CarryOverPlannerTaskEndpoint</c>'s record: the original slot set once,
/// <c>CarryOverCount</c> + 1, back to <c>NotStarted</c> with its time of day kept;</item>
/// <item>a closed block is <c>Cancelled</c> with <see cref="SkipReasonKey.Away"/>, which no skip insight counts.</item>
/// </list>
/// A repeating rule's occurrence is never carried, only closed: the rule places today's own (FO6), and a carried
/// copy would put the same commitment on today twice.
/// </para>
/// </summary>
public class ApplyFreshStartWelcomeBackEndpoint(
    AppDbContext dbContext,
    ICalendarProvisioner calendars,
    IReminderRegistrationService reminders,
    ITodoListItemDueReminderSync dueReminders,
    IMemoryCache undoStore,
    TimeProvider time)
    : Endpoint<ApplyFreshStartRequest, ApplyFreshStartResponse>
{
    protected virtual string[] AllowedRoles() => this.GetDefaultRoles();

    public override void Configure()
    {
        Post("/welcome-back/fresh-start");
        Roles(AllowedRoles());
        Validator<ApplyFreshStartValidator>();
        Summary(s =>
        {
            s.Summary = "Answer the welcome-back card: deal with the overdue to-dos, the open blocks and the unread pile at once";
            s.Description = "Todos: Keep | Someday (clear the due date) | Spread (round-robin over SpreadOverDays days from today, "
                            + "each a postponement). PastTasks: Keep | CloseAsAway (skip, reason 'away') | CarryToToday "
                            + "(carry-over record; a repeating rule's occurrence is closed instead). MarkNotificationsRead. "
                            + "One save; closes the card; returns an undo token valid for 15 minutes.";
            s.Response<ApplyFreshStartResponse>(200, "Applied — the ids of what changed, and the undo token");
            s.Response(400, "An unknown choice, or SpreadOverDays outside 1–14 with Spread");
            s.Response(409, "No welcome-back card is open (never opened, already answered or dismissed, or lapsed), "
                            + "or a concurrent write won the race. Nothing was written");
        });
    }

    public override async Task HandleAsync(ApplyFreshStartRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        var window = await WelcomeBackPile.OpenWindowAsync(dbContext, userId, time.GetUtcNow().UtcDateTime, ct);
        if (window is null)
        {
            AddError("There is no welcome-back card to answer.");
            await Send.ErrorsAsync(this.ErrorStatus(ResultErrorType.Conflict), ct);
            return;
        }

        try
        {
            // Provisioning commits, so today's row is resolved before anything is loaded for writing.
            Calendar? today = req.PastTasks == FreshStartTaskChoice.CarryToToday
                              && await WelcomeBackPile.PastTasks(dbContext, userId, window)
                                  .AnyAsync(t => t.SourceRepeatingTaskId == null, ct)
                ? await calendars.EnsureForDateAsync(userId, window.Today, ct)
                : null;

            var todos = req.Todos == FreshStartTodoChoice.Keep
                ? []
                : await WelcomeBackPile.OverdueTodos(dbContext, userId, window).ToListAsync(ct);
            var tasks = req.PastTasks == FreshStartTaskChoice.Keep
                ? []
                : await WelcomeBackPile.PastTasks(dbContext, userId, window).Include(t => t.Calendar).ToListAsync(ct);
            var notifications = req.MarkNotificationsRead
                ? await WelcomeBackPile.UnreadNotifications(dbContext, userId).ToListAsync(ct)
                : [];

            var todosBefore = RescheduleTodos(todos, req, window.Today);
            var (tasksBefore, closed, carried) = SettleTasks(tasks, req.PastTasks, today);

            var now = time.GetUtcNow().UtcDateTime;
            foreach (var notification in notifications)
            {
                notification.IsRead = true;
                notification.ReadAt = now;
            }

            // The card closes in the same save, so it cannot outlive what it was about, nor close without it.
            var user = await dbContext.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId, ct);
            user.WelcomeBackAwayFrom = null;
            user.WelcomeBackReturnedAt = null;

            await dbContext.SaveChangesAsync(ct);

            // A carried block's day changed and a closed one is done with, so both kinds' reminders follow
            // (the Reminder.RemindAt gotcha), in one call after the commit.
            if (tasks.Count > 0)
                await reminders.SyncForPlannerTasksAsync(tasks.Select(t => t.Id).ToList(), ct);

            // A spread to-do keeps its time of day on its new date, so a timed one gets a reminder again; a
            // "someday" one lost its date and time, so whatever it had is withdrawn.
            if (todos.Count > 0)
                await dueReminders.SyncAsync(todos.Select(i => i.Id).ToList(), ct);

            var token = FreshStartUndo.Store(undoStore, new FreshStartUndo.Snapshot(
                userId, window.AwayFrom, window.ReturnedAt, todosBefore, tasksBefore,
                notifications.Select(n => n.Id).ToList()));

            await Send.OkAsync(new ApplyFreshStartResponse
            {
                UndoToken = token,
                RescheduledTodoIds = todos.Select(i => i.Id).ToList(),
                ClosedTaskIds = closed,
                CarriedTaskIds = carried,
                NotificationsMarkedRead = notifications.Count
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The mapping every write endpoint uses: a lost row_version race is a 409, not a 500.
            var result = DbUtils.HandleException(ex, nameof(HandleAsync));
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(this.ErrorStatus(result.ErrorType), ct);
        }
    }

    /// <summary>
    /// Someday clears the date (and the time that only meant something with it). Spread deals the pile out oldest
    /// first, one per day in turn, so the oldest land today and no day takes more than its share.
    /// </summary>
    private static List<FreshStartUndo.TodoBefore> RescheduleTodos(List<TodoListItem> todos, ApplyFreshStartRequest req, DateOnly today)
    {
        var before = new List<FreshStartUndo.TodoBefore>(todos.Count);
        for (var i = 0; i < todos.Count; i++)
        {
            var item = todos[i];
            var from = item.DueDate;
            var snapshot = (item.DueDate, item.DueTime, item.FirstDueDate, item.PostponeCount);

            if (req.Todos == FreshStartTodoChoice.Someday)
            {
                item.DueDate = null;
                item.DueTime = null;
            }
            else
            {
                item.DueDate = today.AddDays(i % req.SpreadOverDays);
                TodoListItemPostponeRule.Apply(item, wasDone: false, from, item.DueDate, today);
            }

            before.Add(new FreshStartUndo.TodoBefore(
                item.Id, snapshot.DueDate, snapshot.DueTime, snapshot.FirstDueDate, snapshot.PostponeCount, item.DueDate));
        }

        return before;
    }

    private static (List<FreshStartUndo.TaskBefore> Before, List<long> Closed, List<long> Carried) SettleTasks(
        List<PlannerTask> tasks, FreshStartTaskChoice choice, Calendar? today)
    {
        var before = new List<FreshStartUndo.TaskBefore>(tasks.Count);
        var closed = new List<long>();
        var carried = new List<long>();

        foreach (var task in tasks)
        {
            var snapshot = (task.CalendarId, task.Status, task.SkipReason, task.SkipReasonKey, task.ActualStartTime,
                task.ActualEndTime, task.OriginalDate, task.OriginalStartTime, task.CarryOverCount);

            if (choice == FreshStartTaskChoice.CarryToToday && task.SourceRepeatingTaskId == null)
            {
                // CarryOverPlannerTaskEndpoint's record: the original slot as a pair, on the first slip only.
                if (task.OriginalDate is null)
                {
                    task.OriginalDate = task.Calendar.Date;
                    task.OriginalStartTime = task.StartTime;
                }
                task.CarryOverCount++;
                task.CalendarId = today!.Id;
                task.ApplyStatus(PlannerTaskStatus.NotStarted);
                carried.Add(task.Id);
            }
            else
            {
                task.ApplyStatus(PlannerTaskStatus.Cancelled, skipReasonKey: SkipReasonKey.Away);
                closed.Add(task.Id);
            }

            before.Add(new FreshStartUndo.TaskBefore(
                task.Id, snapshot.CalendarId, snapshot.Status, snapshot.SkipReason, snapshot.SkipReasonKey,
                snapshot.ActualStartTime, snapshot.ActualEndTime, snapshot.OriginalDate, snapshot.OriginalStartTime,
                snapshot.CarryOverCount, task.CalendarId, task.Status));
        }

        return (before, closed, carried);
    }
}
