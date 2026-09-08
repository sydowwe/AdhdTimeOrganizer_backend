using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.application.eventHandler;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// <c>SEC-1</c> (<c>AdhdTimeOrganizer.Planning/review/02-findings.md</c>) — the four ids a planner write
/// carries but does not mint: <c>ActivityId</c>, <c>ImportanceId</c> and <c>TodolistId</c> on
/// <c>PlannerTaskRequest</c>, and <c>DefaultApplyTemplateId</c> on <c>UserPlannerSettingsRequest</c>.
/// </summary>
/// <remarks>
/// <para>
/// The FK proves the row exists, never that it is the caller's, and the task is stamped with the caller's
/// own <c>UserId</c> — so a task naming a stranger's activity inserts cleanly. This one is not even
/// invisible: <c>PlannerTask.Color</c> reads through <c>Activity.Role.Color</c> and
/// <c>PlannerTaskResponse</c> projects the same navigation, so the victim's role colour, name and category
/// come back on every read of the attacker's own day. That is why the checks assert on rows and on the
/// day-plan read, not only on the status code.
/// </para>
/// <para>
/// The guard is <c>PlannerReferencedIdOwnershipExtensions</c>, called from <c>BeforeMapping</c> on create
/// and update and from the top of both apply-template handlers. 404 rather than 403, and the same message
/// for a missing id as for a foreign one, for the same existence-oracle reason as
/// <see cref="TrackerMappingTargetOwnershipTests"/> and <see cref="ActivityIdOwnershipTests"/>.
/// <c>TodolistId</c> is the one Planning cannot answer itself — it goes back out through Core's
/// <c>IReferencedIdOwnershipService</c> and the <c>IUserOwnedReferenceVerifier</c> seam.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class PlannerReferencedIdOwnershipTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private const string TaskRoute = "/api/planner-task";
    private const string SettingsRoute = "/api/planner/settings";
    private const string ApplyTemplateRoute = "/api/calendar/apply-planner-template";
    private const string ApplyTemplateBatchRoute = "/api/calendar/apply-planner-template/batch";

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private long _ownTaskPriorityId;

    protected override async Task SeedAsync(DbContext db)
    {
        var priority = new TaskPriority { UserId = UserId, Text = "Normal", Color = "#556677", Priority = 1 };
        db.Set<TaskPriority>().Add(priority);
        await db.SaveChangesAsync(CancellationToken);
        _ownTaskPriorityId = priority.Id;
    }

    // ---- ActivityId --------------------------------------------------------------------------------

    [Fact(DisplayName = "SEC-1: creating a planner task against another user's activity is rejected")]
    public async Task CreatePlannerTask_WithForeignActivityId_IsRejected()
    {
        var (_, foreignActivityId) = await SeedForeignUserWithActivityAsync("foreign-create");
        var calendarId = await SeedOwnCalendarAsync(Today);

        var response = await CreateClient().PostAsJsonAsync(
            TaskRoute, TaskBody(foreignActivityId, 9, calendarId: calendarId), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "an activity the caller does not own must read as one that does not exist");
        (await CountTasksAsync(t => t.ActivityId == foreignActivityId)).Should().Be(0);
    }

    /// <summary>The control — without it the endpoint could be hard-wired to 404 and every case above would pass.</summary>
    [Fact(DisplayName = "SEC-1: creating a planner task against the caller's own activity still succeeds")]
    public async Task CreatePlannerTask_WithOwnActivityId_Succeeds()
    {
        long ownActivityId;
        await using (var db = CreateDbContext())
            ownActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "own-create", UserId, CancellationToken);
        var calendarId = await SeedOwnCalendarAsync(Today);

        var response = await CreateClient().PostAsJsonAsync(
            TaskRoute, TaskBody(ownActivityId, 10, calendarId: calendarId), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await CountTasksAsync(t => t.ActivityId == ownActivityId)).Should().Be(1);
    }

    /// <summary>
    /// Ordering, not just presence. <c>CreatePlannerTaskEndpoint</c> resolves the day through
    /// <c>ICalendarProvisioner</c>, which <b>commits</b> — so a guard placed after it would leave a calendar
    /// row behind on a request that then 404s, and a lazily-created day is a user-visible artefact.
    /// </summary>
    [Fact(DisplayName = "SEC-1: a rejected create by date leaves no lazily-provisioned calendar row behind")]
    public async Task CreatePlannerTask_WithForeignActivityIdByDate_ProvisionsNoCalendar()
    {
        var (_, foreignActivityId) = await SeedForeignUserWithActivityAsync("foreign-lazy");
        var date = new DateOnly(2034, 7, 11);

        (await FindOwnCalendarAsync(date)).Should().BeNull("the case is only meaningful on a date with no row");

        var response = await CreateClient().PostAsJsonAsync(
            TaskRoute, TaskBody(foreignActivityId, 9, date: date), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await FindOwnCalendarAsync(date)).Should().BeNull(
            "the ownership check runs before provisioning, so a request on its way to a 404 creates nothing");
    }

    // ---- ImportanceId ------------------------------------------------------------------------------

    [Fact(DisplayName = "SEC-1: creating a planner task with another user's importance is rejected")]
    public async Task CreatePlannerTask_WithForeignImportanceId_IsRejected()
    {
        var foreignUserId = await SeedForeignUserAsync();
        long ownActivityId, foreignImportanceId;
        await using (var db = CreateDbContext())
        {
            ownActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "own-importance", UserId, CancellationToken);
            foreignImportanceId = await PlanningTestSeedHelper.SeedTaskImportanceAsync(db, 5, foreignUserId, ct: CancellationToken);
        }
        var calendarId = await SeedOwnCalendarAsync(Today);

        var response = await CreateClient().PostAsJsonAsync(
            TaskRoute, TaskBody(ownActivityId, 11, calendarId: calendarId, importanceId: foreignImportanceId),
            JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "ImportanceId is checked independently of ActivityId — a case that only checks the activity is a silent partial regression");
        (await CountTasksAsync(t => t.ImportanceId == foreignImportanceId)).Should().Be(0);
    }

    /// <summary>The control for the importance check: it is a legitimate optional field.</summary>
    [Fact(DisplayName = "SEC-1: creating a planner task with the caller's own importance still succeeds")]
    public async Task CreatePlannerTask_WithOwnImportanceId_Succeeds()
    {
        long ownActivityId, ownImportanceId;
        await using (var db = CreateDbContext())
        {
            ownActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "own-importance-ok", UserId, CancellationToken);
            ownImportanceId = await PlanningTestSeedHelper.SeedTaskImportanceAsync(db, 7, UserId, ct: CancellationToken);
        }
        var calendarId = await SeedOwnCalendarAsync(Today);

        var response = await CreateClient().PostAsJsonAsync(
            TaskRoute, TaskBody(ownActivityId, 12, calendarId: calendarId, importanceId: ownImportanceId),
            JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await CountTasksAsync(t => t.ImportanceId == ownImportanceId)).Should().Be(1);
    }

    // ---- TodolistId (the cross-slice one) ----------------------------------------------------------

    /// <summary>
    /// The id Planning cannot check locally: <c>PlannerTask.TodolistItemId</c> is the navigation-free FK the
    /// host declares, and the answer comes back through Core's <c>IReferencedIdOwnershipService</c> over the
    /// <c>IUserOwnedReferenceVerifier</c> seam. It is also the half that makes <c>SEC-2</c> exploitable —
    /// without it, an attacker can aim their own task at a victim's item id.
    /// </summary>
    [Fact(DisplayName = "SEC-1: linking a planner task to another user's to-do item is rejected")]
    public async Task CreatePlannerTask_WithForeignTodolistId_IsRejected()
    {
        long ownActivityId;
        await using (var db = CreateDbContext())
            ownActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "own-todo-link", UserId, CancellationToken);

        var (_, foreignItemId) = await SeedForeignUserWithTodoItemAsync("foreign-todo");
        var calendarId = await SeedOwnCalendarAsync(Today);

        var response = await CreateClient().PostAsJsonAsync(
            TaskRoute, TaskBody(ownActivityId, 13, calendarId: calendarId, todolistId: foreignItemId),
            JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CountTasksAsync(t => t.TodolistItemId == foreignItemId)).Should().Be(0);
    }

    /// <summary>
    /// The control, and the one that would catch the seam failing <b>closed</b> for everyone: a slice dropped
    /// from <c>ModuleAssemblies</c> or a renamed key makes <c>OwnsReferenceAsync</c> answer <c>false</c> for
    /// every id, and every negative case above would still pass.
    /// </summary>
    [Fact(DisplayName = "SEC-1: linking a planner task to the caller's own to-do item still succeeds")]
    public async Task CreatePlannerTask_WithOwnTodolistId_Succeeds()
    {
        long ownActivityId, ownItemId;
        await using (var db = CreateDbContext())
        {
            ownActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "own-todo-ok", UserId, CancellationToken);
            var item = new TodoListItem { UserId = UserId, ActivityId = ownActivityId, TaskPriorityId = _ownTaskPriorityId };
            db.Set<TodoListItem>().Add(item);
            await db.SaveChangesAsync(CancellationToken);
            ownItemId = item.Id;
        }
        var calendarId = await SeedOwnCalendarAsync(Today);

        var response = await CreateClient().PostAsJsonAsync(
            TaskRoute, TaskBody(ownActivityId, 14, calendarId: calendarId, todolistId: ownItemId),
            JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "the seam fails closed, so a broken registration would reject this too");
        (await CountTasksAsync(t => t.TodolistItemId == ownItemId)).Should().Be(1);
    }

    // ---- the update surface ------------------------------------------------------------------------

    /// <summary>
    /// The more dangerous of the two writes: it can re-point a task the caller legitimately owns without
    /// creating anything, so there is no new row for anybody to notice.
    /// </summary>
    [Fact(DisplayName = "SEC-1: re-pointing a planner task onto another user's activity is rejected")]
    public async Task UpdatePlannerTask_WithForeignActivityId_IsRejected()
    {
        var (_, foreignActivityId) = await SeedForeignUserWithActivityAsync("foreign-update");

        long ownActivityId, taskId;
        await using (var db = CreateDbContext())
        {
            ownActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "own-update", UserId, CancellationToken);
            var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, Today, UserId, CancellationToken);
            taskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(
                db, ownActivityId, calendarId, new TimeOnly(15, 0), new TimeOnly(16, 0), UserId, ct: CancellationToken);
        }

        var response = await CreateClient().PutAsJsonAsync(
            $"{TaskRoute}/{taskId}", TaskBody(foreignActivityId, 15, date: Today), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var assertDb = CreateDbContext();
        var after = await assertDb.Set<PlannerTask>().IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(t => t.Id == taskId, CancellationToken);
        after.ActivityId.Should().Be(ownActivityId,
            "the guard runs before the entity is mapped, so a rejected update writes nothing at all");
    }

    // ---- apply-template ----------------------------------------------------------------------------

    /// <summary>
    /// <c>PlannerTemplateApplier.BuildTasks</c> takes the same <c>PlannerTaskRequest</c> shape straight off
    /// the wire, one per template line, so the hole is identical and repeated N times. The single-day
    /// endpoint is checked here; the batch one shares the guard and the applier.
    /// </summary>
    [Fact(DisplayName = "SEC-1: applying a template whose lines name another user's activity is rejected")]
    public async Task ApplyTemplate_WithForeignActivityIdInATask_IsRejected()
    {
        var (_, foreignActivityId) = await SeedForeignUserWithActivityAsync("foreign-template");
        var date = new DateOnly(2034, 7, 13);

        long ownActivityId, templateId, calendarId;
        await using (var db = CreateDbContext())
        {
            ownActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "own-template", UserId, CancellationToken);
            templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, UserId, ct: CancellationToken);
            calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, date, UserId, CancellationToken);
        }

        var response = await CreateClient().PostAsJsonAsync(ApplyTemplateRoute, new
        {
            TemplateId = templateId,
            CalendarId = calendarId,
            ConflictResolution = "Ignore",
            TasksFromTemplate = new[]
            {
                TaskBody(ownActivityId, 8, calendarId: calendarId),
                TaskBody(foreignActivityId, 9, calendarId: calendarId)
            }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await CountTasksAsync(t => t.CalendarId == calendarId)).Should().Be(0,
            "one bad line rejects the whole apply — the legitimate line beside it must not be written either");
    }

    /// <summary>
    /// The batch apply is the one where <b>where</b> the guard sits is load-bearing, not just whether it
    /// exists. <c>ApplyTemplateBatchPlannerTaskEndpoint</c> applies and commits each day on its own, in
    /// request order, precisely so that one bad day cannot undo the days before it — so a guard moved into
    /// the per-day loop (or re-derived per day) would answer 404 having already written the foreign activity
    /// onto every day ahead of the failure, and the status code alone would look identical. Three days, so a
    /// per-day check would have committed two of them.
    /// </summary>
    [Fact(DisplayName = "SEC-1: a batch apply naming another user's activity writes to none of the days")]
    public async Task ApplyTemplateBatch_WithForeignActivityId_WritesNoDay()
    {
        var (_, foreignActivityId) = await SeedForeignUserWithActivityAsync("foreign-template-batch");

        long ownActivityId, templateId;
        long[] calendarIds;
        await using (var db = CreateDbContext())
        {
            ownActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "own-template-batch", UserId, CancellationToken);
            templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, UserId, ct: CancellationToken);
            calendarIds =
            [
                await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2034, 7, 21), UserId, CancellationToken),
                await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2034, 7, 22), UserId, CancellationToken),
                await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2034, 7, 23), UserId, CancellationToken)
            ];
        }

        var response = await CreateClient().PostAsJsonAsync(ApplyTemplateBatchRoute, new
        {
            TemplateId = templateId,
            CalendarIds = calendarIds,
            ConflictResolution = "Ignore",
            TasksFromTemplate = new[]
            {
                TaskBody(ownActivityId, 8),
                TaskBody(foreignActivityId, 9)
            }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the task list is shared by every day, so a foreign id in it is a bad request for the whole batch — not a per-day succeeded:false");

        (await CountTasksAsync(t => calendarIds.Contains(t.CalendarId))).Should().Be(0,
            "the check runs once, before the first day is applied and committed");

        await using var assertDb = CreateDbContext();
        var template = await assertDb.Set<TaskPlannerDayTemplate>().SingleAsync(t => t.Id == templateId, CancellationToken);
        template.UsageCount.Should().Be(0);
        (await assertDb.Set<Calendar>().CountAsync(c => calendarIds.Contains(c.Id) && c.AppliedTemplateId != null, CancellationToken))
            .Should().Be(0, "a day stamped with the template it never received is the trace a per-day guard would leave");
    }

    // ---- DefaultApplyTemplateId --------------------------------------------------------------------

    /// <summary>
    /// The integrity-only member of the family: <c>UserPlannerSettingsResponse</c> returns the bare id and no
    /// name, so nothing leaks — it just stores a pointer at a row this user can never resolve, and
    /// default-apply then silently does nothing.
    /// </summary>
    [Fact(DisplayName = "SEC-1: pointing DefaultApplyTemplateId at another user's template is rejected")]
    public async Task UpdatePlannerSettings_WithForeignDefaultApplyTemplateId_IsRejected()
    {
        var foreignUserId = await SeedForeignUserAsync();
        long foreignTemplateId;
        await using (var db = CreateDbContext())
            foreignTemplateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(
                db, foreignUserId, ct: CancellationToken);

        var response = await CreateClient().PutAsJsonAsync(
            SettingsRoute, SettingsBody(foreignTemplateId), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<UserPlannerSettings>().IgnoreQueryFilters()
            .CountAsync(s => s.DefaultApplyTemplateId == foreignTemplateId, CancellationToken))
            .Should().Be(0);
    }

    [Fact(DisplayName = "SEC-1: pointing DefaultApplyTemplateId at the caller's own template still succeeds")]
    public async Task UpdatePlannerSettings_WithOwnDefaultApplyTemplateId_Succeeds()
    {
        long ownTemplateId;
        await using (var db = CreateDbContext())
            ownTemplateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, UserId, ct: CancellationToken);

        var response = await CreateClient().PutAsJsonAsync(
            SettingsRoute, SettingsBody(ownTemplateId), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var assertDb = CreateDbContext();
        var settings = await assertDb.Set<UserPlannerSettings>().SingleAsync(s => s.UserId == UserId, CancellationToken);
        settings.DefaultApplyTemplateId.Should().Be(ownTemplateId);
    }

    // ---- helpers -----------------------------------------------------------------------------------

    private static object TaskBody(
        long activityId, int hour, DateOnly? date = null, long? calendarId = null,
        long? importanceId = null, long? todolistId = null) => new
    {
        StartTime = new { Hours = hour, Minutes = 0 },
        EndTime = new { Hours = hour + 1, Minutes = 0 },
        IsBackground = false,
        ActivityId = activityId,
        ImportanceId = importanceId,
        TodolistId = todolistId,
        Status = "NotStarted",
        Date = date,
        CalendarId = calendarId
    };

    private static object SettingsBody(long? defaultApplyTemplateId) => new
    {
        RemindersEnabled = true,
        ReminderMinutesBefore = 10,
        DetailsPanelExpandedByDefault = true,
        ArrowKeyNavEnabled = true,
        PredefinedSkipReasons = new[] { "Not needed today" },
        SlotDurationMinutes = 15,
        DefaultApplyTemplateId = defaultApplyTemplateId,
        DefaultConflictResolution = "Ignore",
        DefaultApplyPreviewMode = true
    };

    private Task<long> SeedForeignUserAsync() =>
        ActivityMergeTestSupport.SecondUserAsync(Fixture, $"planner-ownership-{Guid.NewGuid():N}@test.com");

    private async Task<(long UserId, long ActivityId)> SeedForeignUserWithActivityAsync(string name)
    {
        var foreignUserId = await SeedForeignUserAsync();
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, name, foreignUserId, CancellationToken);
        return (foreignUserId, activityId);
    }

    private async Task<(long UserId, long TodoItemId)> SeedForeignUserWithTodoItemAsync(string name)
    {
        var foreignUserId = await SeedForeignUserAsync();
        await using var db = CreateDbContext();

        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, name, foreignUserId, CancellationToken);
        var priority = new TaskPriority { UserId = foreignUserId, Text = "Foreign", Color = "#000000", Priority = 1 };
        db.Set<TaskPriority>().Add(priority);
        await db.SaveChangesAsync(CancellationToken);

        var item = new TodoListItem { UserId = foreignUserId, ActivityId = activityId, TaskPriorityId = priority.Id };
        db.Set<TodoListItem>().Add(item);
        await db.SaveChangesAsync(CancellationToken);

        return (foreignUserId, item.Id);
    }

    private async Task<long> SeedOwnCalendarAsync(DateOnly date)
    {
        await using var db = CreateDbContext();
        return await PlanningTestSeedHelper.SeedCalendarAsync(db, date, UserId, CancellationToken);
    }

    private async Task<Calendar?> FindOwnCalendarAsync(DateOnly date)
    {
        await using var db = CreateDbContext();
        return await db.Set<Calendar>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.UserId == UserId && c.Date == date, CancellationToken);
    }

    private async Task<int> CountTasksAsync(System.Linq.Expressions.Expression<Func<PlannerTask, bool>> predicate)
    {
        await using var db = CreateDbContext();
        return await db.Set<PlannerTask>().IgnoreQueryFilters().CountAsync(predicate, CancellationToken);
    }
}

/// <summary>
/// <c>SEC-2</c> — <c>TodoListItemIsDoneChangedEventHandler</c>'s missing <c>UserId</c> predicate, the other
/// half of <see cref="PlannerReferencedIdOwnershipTests"/>.
/// </summary>
/// <remarks>
/// <para>
/// The handler resolves its <c>DbContext</c> from a <b>fresh scope</b>, and the global user filter is
/// <c>context.ScopeUserId == null || e.UserId == context.ScopeUserId</c> with <c>ScopeUserId</c> answering
/// <c>null</c> whenever there is no authenticated principal. Under HTTP dispatch the ambient
/// <c>HttpContext</c> flows into the new scope and the filter binds to the publisher, which is why the hole
/// had no symptom — but a publish from a scheduled job, a console seeder or a future background fan-out
/// makes the filter a no-op, and the handler then flips <b>every</b> user's planner task linked to that item
/// id. Its Routines sibling has always carried the predicate.
/// </para>
/// <para>
/// Driven against the handler directly, as <c>CompletionFanOutTests.PlannerTaskEvent_CannotTouchAnotherUsersTodoItem</c>
/// is and for the same reason: the real endpoint always builds the event from the acting user's own entity,
/// so HTTP cannot forge the id collision. Invoking it from the unauthenticated factory's scope is also the
/// only way to reproduce the null-<c>ScopeUserId</c> condition that turns the filter off — which is the
/// actual precondition of the finding, not an artificial one.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class TodoListItemFanOutUserScopingTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact(DisplayName = "SEC-2: a to-do fan-out published with no principal cannot flip another user's planner task")]
    public async Task TodoListItemEvent_CannotFlipAnotherUsersPlannerTask()
    {
        var victimUserId = await ActivityMergeTestSupport.SecondUserAsync(
            Fixture, $"todo-fanout-scoping-{Guid.NewGuid():N}@test.com");

        long sharedItemId, attackerTaskId, victimTaskId;
        await using (var db = CreateDbContext())
        {
            var attackerActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "fanout-attacker", UserId, CancellationToken);
            var victimActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "fanout-victim", victimUserId, CancellationToken);

            var priority = new TaskPriority { UserId = UserId, Text = "Normal", Color = "#556677", Priority = 1 };
            db.Set<TaskPriority>().Add(priority);
            await db.SaveChangesAsync(CancellationToken);

            var item = new TodoListItem { UserId = UserId, ActivityId = attackerActivityId, TaskPriorityId = priority.Id };
            db.Set<TodoListItem>().Add(item);
            await db.SaveChangesAsync(CancellationToken);
            sharedItemId = item.Id;

            var attackerCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, Today, UserId, CancellationToken);
            var victimCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, Today, victimUserId, CancellationToken);

            attackerTaskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(
                db, attackerActivityId, attackerCalendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), UserId, ct: CancellationToken);

            // The pre-SEC-1 state, seeded directly because the create endpoint now refuses to write it: the
            // victim's task carrying the attacker's item id. This is the row the missing predicate reached.
            victimTaskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(
                db, victimActivityId, victimCalendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), victimUserId, ct: CancellationToken);

            foreach (var (id, itemId) in new[] { (attackerTaskId, sharedItemId), (victimTaskId, sharedItemId) })
            {
                var task = await db.Set<PlannerTask>().IgnoreQueryFilters().SingleAsync(t => t.Id == id, CancellationToken);
                task.TodolistItemId = itemId;
            }
            await db.SaveChangesAsync(CancellationToken);
        }

        var scopeFactory = Fixture.UnauthenticatedFactory.Services.GetRequiredService<IServiceScopeFactory>();
        var handler = new TodoListItemIsDoneChangedEventHandler(
            scopeFactory, NullLogger<TodoListItemIsDoneChangedEventHandler>.Instance);

        await handler.HandleAsync(new TodoListItemIsDoneChangedEvent(sharedItemId, UserId, true), CancellationToken);

        await using var verifyDb = CreateDbContext();
        var tasks = await verifyDb.Set<PlannerTask>().IgnoreQueryFilters()
            .Where(t => t.Id == attackerTaskId || t.Id == victimTaskId)
            .ToDictionaryAsync(t => t.Id, CancellationToken);

        tasks[attackerTaskId].Status.Should().Be(PlannerTaskStatus.Completed,
            "the publisher's own task still has to follow the item — the control for the assertion below");
        tasks[victimTaskId].Status.Should().Be(PlannerTaskStatus.NotStarted,
            "the handler's UserId predicate is what keeps one user's fan-out off another user's day, with the query filter off");
    }

    /// <summary>
    /// The same rule over the real route, which is the half <c>SEC-1</c> leaves behind: a task pointing at a
    /// stranger's to-do item can no longer be <i>created</i>, but rows written before the guard existed still
    /// carry one, and this is the request that would fire on them.
    /// </summary>
    /// <remarks>
    /// It passes for a different reason than the test above, which is why both are here. Under HTTP the
    /// ambient <c>HttpContext</c> flows into the handler's fresh scope, so the global query filter binds to
    /// the toggling user and hides the other user's task regardless of the handler's own predicate — this
    /// pinned the accidental pass before <c>SEC-2</c> and pins the deliberate one after it. The pair is the
    /// point: remove the predicate and only the sibling fails; lose the filter and only this one does.
    /// </remarks>
    [Fact(DisplayName = "SEC-2: toggling a to-do item over HTTP cannot flip another user's task linked to it")]
    public async Task TodoListItemToggle_AsItemOwner_LeavesAnotherUsersLinkedTaskAlone()
    {
        var itemOwnerId = await ActivityMergeTestSupport.SecondUserAsync(
            Fixture, $"todo-toggle-scoping-{Guid.NewGuid():N}@test.com");

        long itemId, itemOwnerTaskId, otherUsersTaskId;
        await using (var db = CreateDbContext())
        {
            var itemOwnerActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "toggle-owner", itemOwnerId, CancellationToken);
            var otherActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "toggle-other", UserId, CancellationToken);

            var priority = new TaskPriority { UserId = itemOwnerId, Text = "Normal", Color = "#556677", Priority = 1 };
            db.Set<TaskPriority>().Add(priority);
            await db.SaveChangesAsync(CancellationToken);

            var item = new TodoListItem { UserId = itemOwnerId, ActivityId = itemOwnerActivityId, TaskPriorityId = priority.Id };
            db.Set<TodoListItem>().Add(item);
            await db.SaveChangesAsync(CancellationToken);
            itemId = item.Id;

            // Both on today: the handler only follows the item onto the user's current day.
            var itemOwnerCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, Today, itemOwnerId, CancellationToken);
            var otherCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, Today, UserId, CancellationToken);

            itemOwnerTaskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(
                db, itemOwnerActivityId, itemOwnerCalendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), itemOwnerId, ct: CancellationToken);

            // The pre-SEC-1 row: another user's task carrying the item owner's item id. Seeded directly
            // because POST /planner-task now refuses to write it.
            otherUsersTaskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(
                db, otherActivityId, otherCalendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), UserId, ct: CancellationToken);

            foreach (var id in new[] { itemOwnerTaskId, otherUsersTaskId })
            {
                var task = await db.Set<PlannerTask>().IgnoreQueryFilters().SingleAsync(t => t.Id == id, CancellationToken);
                task.TodolistItemId = itemId;
            }
            await db.SaveChangesAsync(CancellationToken);
        }

        await using var itemOwnerFactory = CreateFactory(["User"], itemOwnerId);
        var response = await itemOwnerFactory.CreateClient().PatchAsJsonAsync(
            "api/todo-list-item/toggle-is-done", new { Ids = new[] { itemId }, ForceValue = true }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var verifyDb = CreateDbContext();
        var after = await verifyDb.Set<PlannerTask>().IgnoreQueryFilters()
            .Where(t => t.Id == itemOwnerTaskId || t.Id == otherUsersTaskId)
            .ToDictionaryAsync(t => t.Id, CancellationToken);

        after[itemOwnerTaskId].Status.Should().Be(PlannerTaskStatus.Completed,
            "the fan-out still has to reach the toggling user's own day — the control for the assertion below");
        after[otherUsersTaskId].Status.Should().Be(PlannerTaskStatus.NotStarted,
            "one user ticking their to-do item must not complete work on somebody else's day");
    }
}
