using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.application.eventHandler;
using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.Planning.application.service.reminder;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sydowwe.Framework.Testing;
using Sydowwe.Reminders.domain.entity;
using Sydowwe.Reminders.domain.@enum;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-16 — the completion fan-out event handlers (<c>PlannerTaskIsDoneChangedEventHandler</c>,
/// <c>TodoListItemIsDoneChangedEventHandler</c>, <c>RoutineTodoListIsDoneChangedEventHandler</c>).
/// </summary>
/// <remarks>
/// The originating prompt (<c>review/portal/testingPrompts/CompletionFanOutEventHandlers.md</c>) was
/// written against an earlier state of the handlers and is stale on several points, reconfirmed by
/// reading the current source directly rather than trusting the prompt:
/// <list type="bullet">
/// <item><b>CQ-6 (Scenario A)</b> — <c>SyncTodoListItem</c>/<c>SyncRoutineTodoList</c> already
/// <c>.Include(x => x.Steps)</c> and <c>BaseTodoListItem.SetDone</c> already snaps every step's
/// <c>IsDone</c>. The steps-desync bug the prompt describes does not exist on current <c>main</c> — the
/// test below is written as a normal (green) regression pin, not a <c>KnownGap</c>.</item>
/// <item><b>CQ-7 (Scenario B)</b> — <c>TodoListItemIsDoneChangedEventHandler</c> already excludes
/// <c>Cancelled</c> tasks, already calls <c>reminders.SyncForPlannerTasksAsync</c>, and
/// <c>PlannerTask.ApplyStatus</c> already clears actual times for <c>NotStarted</c>/<c>Cancelled</c>.
/// All three sub-scenarios are written as normal (green) pins, not <c>KnownGap</c>.</item>
/// <item><b>CQ-8 (Scenario D)</b> — <c>ActivityAddedToHistoryEventHandler</c> and
/// <c>ActivityCreatedIsOnToDoListEventHandler</c>, and their events, do not exist anywhere in the repo
/// (host or <c>framework/</c> submodule). Current <c>docs/domain-map.md</c> states they were "removed as
/// dead code — no publisher ever existed for either," and <c>Activity</c> has no
/// is-on-to-do-list-style flag to drive the scenario the prompt describes. There is nothing left to
/// write a test against, so Scenario D is intentionally not implemented here — see the comment at the
/// bottom of this file instead of a test method.</item>
/// <item><b>SEC-8 (Scenario E)</b> — <c>PlannerTaskIsDoneChangedEventHandler.SyncTodoListItem</c>
/// already filters by <c>i.UserId == eventModel.UserId</c>, not by id alone as the prompt claims. The
/// test below still exercises the isolation directly against the handler (HTTP cannot forge a
/// cross-user event — the real endpoint always builds it from the acting user's own entity), because
/// that predicate is exactly the thing worth pinning against regression.</item>
/// </list>
/// Only Scenario F (concurrency) reproduces a genuine, currently-red gap and is tagged accordingly.
/// </remarks>
[Collection("Postgres")]
public class CompletionFanOutTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private long _taskPriorityId;

    protected override async Task SeedAsync(DbContext db)
    {
        var priority = new TaskPriority { UserId = UserId, Text = "Normal", Color = "#556677", Priority = 1 };
        db.Set<TaskPriority>().Add(priority);
        await db.SaveChangesAsync(CancellationToken);
        _taskPriorityId = priority.Id;
    }

    // ---- A: CQ-6 ------------------------------------------------------------------------------------

    [Fact(DisplayName = "CQ-6: completing the planner task snaps the linked to-do item's steps too, and stays consistent when a step is reopened")]
    public async Task PlannerTaskCompleted_SyncsTodoItemAndItsSteps()
    {
        long todoItemId, plannerTaskId;
        List<Guid> stepIds;
        await using (var db = CreateDbContext())
        {
            var (_, itemId, taskId) = await SeedLinkedTodoAndPlannerTaskAsync(db, "cq6-report", totalCount: 3, stepCount: 3);
            todoItemId = itemId;
            plannerTaskId = taskId;
            stepIds = (await db.Set<TodoListItem>().Include(i => i.Steps).SingleAsync(i => i.Id == itemId, CancellationToken))
                .Steps.Select(s => s.Id).ToList();
        }

        var response = await CreateUserRoleClient().PatchAsJsonAsync(
            $"api/planner-task/{plannerTaskId}/status", new { Status = "Completed" }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var db = CreateDbContext())
        {
            var item = await db.Set<TodoListItem>().Include(i => i.Steps).SingleAsync(i => i.Id == todoItemId, CancellationToken);
            item.IsDone.Should().BeTrue();
            item.DoneCount.Should().Be(item.TotalCount);
            item.Steps.Should().OnlyContain(s => s.IsDone, "the planner-task fan-out must snap every step, not just the counters");
        }

        // The follow-on check: reopen one step through the ordinary step endpoint and confirm the
        // arithmetic still lands cleanly, rather than desyncing from steps the fan-out left stale.
        var toggle = await CreateUserRoleClient().PatchAsync(
            $"api/todo-list-item/{todoItemId}/steps/{stepIds[0]}/toggle", new StringContent(string.Empty), CancellationToken);
        toggle.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var finalDb = CreateDbContext();
        var final = await finalDb.Set<TodoListItem>().Include(i => i.Steps).SingleAsync(i => i.Id == todoItemId, CancellationToken);
        final.DoneCount.Should().Be(2, "one of three steps was reopened");
        final.IsDone.Should().BeFalse();
    }

    // ---- B: CQ-7 --------------------------------------------------------------------------------------

    [Fact(DisplayName = "CQ-7: toggling the to-do item leaves an already-Cancelled planner task alone")]
    public async Task TogglingTodoItem_LeavesCancelledPlannerTaskAlone()
    {
        long todoItemId, plannerTaskId;
        await using (var db = CreateDbContext())
        {
            var (_, itemId, taskId) = await SeedLinkedTodoAndPlannerTaskAsync(db, "cq7-cancelled", taskStatus: PlannerTaskStatus.Cancelled);
            todoItemId = itemId;
            plannerTaskId = taskId;
        }

        var toggle = await ToggleTodoItemAsync(todoItemId, true);
        toggle.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db2 = CreateDbContext();
        var task = await db2.Set<PlannerTask>().SingleAsync(t => t.Id == plannerTaskId, CancellationToken);
        task.Status.Should().Be(PlannerTaskStatus.Cancelled,
            "a task the user deliberately cancelled must not be silently un-cancelled or completed by the to-do fan-out");
    }

    [Fact(DisplayName = "CQ-7: toggling the to-do item retires the reminder on its linked planner task")]
    public async Task TogglingTodoItem_RetiresLinkedPlannerTaskReminder()
    {
        long todoItemId, plannerTaskId;
        await using (var db = CreateDbContext())
        {
            var (_, itemId, taskId) = await SeedLinkedTodoAndPlannerTaskAsync(db, "cq7-reminder");
            todoItemId = itemId;
            plannerTaskId = taskId;
        }

        var createdReminder = await CreateClient().PostAsJsonAsync("api/reminder", new
        {
            Title = "Stand-up",
            RemindAt = DateTime.UtcNow.AddHours(2),
            LeadOffsetsMinutes = new[] { 0 },
            PlannerTaskId = plannerTaskId
        }, JsonOpts, CancellationToken);
        createdReminder.StatusCode.Should().Be(HttpStatusCode.Created);
        var reminderId = await createdReminder.Content.ReadFromJsonAsync<long>(CancellationToken);

        var toggle = await ToggleTodoItemAsync(todoItemId, true);
        toggle.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db2 = CreateDbContext();
        var definition = await db2.Set<ReminderDefinition>()
            .Where(d => d.OwnerModule == ReminderRegistrationService.OwnerModule
                        && d.SubjectType == ReminderRegistrationService.SubjectType
                        && d.SubjectId == reminderId.ToString()
                        && d.Kind == ReminderRegistrationService.Kind)
            .SingleAsync(CancellationToken);

        definition.Status.Should().Be(ReminderStatus.Cancelled,
            "finishing the linked planner task via the to-do fan-out must retire its reminder, the same as a direct status patch does");
    }

    [Fact(DisplayName = "CQ-7: un-ticking the to-do item clears the linked planner task's actual times")]
    public async Task UntickingTodoItem_ClearsPlannerTaskActualTimes()
    {
        long todoItemId, plannerTaskId;
        await using (var db = CreateDbContext())
        {
            var (_, itemId, taskId) = await SeedLinkedTodoAndPlannerTaskAsync(db, "cq7-actualtimes", taskStatus: PlannerTaskStatus.Completed);
            todoItemId = itemId;
            plannerTaskId = taskId;

            // Seeded directly rather than through the endpoints, so the pre-state (task Completed with
            // actual times, item done) has to be assembled by hand to stay consistent with itself.
            var task = await db.Set<PlannerTask>().SingleAsync(t => t.Id == taskId, CancellationToken);
            task.ActualStartTime = new TimeOnly(9, 5);
            task.ActualEndTime = new TimeOnly(9, 55);
            var item = await db.Set<TodoListItem>().SingleAsync(i => i.Id == itemId, CancellationToken);
            item.IsDone = true;
            await db.SaveChangesAsync(CancellationToken);
        }

        var untick = await ToggleTodoItemAsync(todoItemId, false);
        untick.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db2 = CreateDbContext();
        var finalTask = await db2.Set<PlannerTask>().SingleAsync(t => t.Id == plannerTaskId, CancellationToken);
        finalTask.Status.Should().Be(PlannerTaskStatus.NotStarted);
        finalTask.ActualStartTime.Should().BeNull();
        finalTask.ActualEndTime.Should().BeNull();
    }

    // ---- C: round-trip / idempotency -------------------------------------------------------------------

    [Fact(DisplayName = "Round trip: done -> undone -> done keeps DoneCount pinned at 0 or TotalCount, never in between")]
    public async Task RoundTrip_KeepsDoneCountPinned()
    {
        long todoItemId, plannerTaskId;
        await using (var db = CreateDbContext())
        {
            var (_, itemId, taskId) = await SeedLinkedTodoAndPlannerTaskAsync(db, "roundtrip", totalCount: 2, stepCount: 2);
            todoItemId = itemId;
            plannerTaskId = taskId;
        }

        var client = CreateUserRoleClient();

        async Task SetStatusAsync(string status)
        {
            var r = await client.PatchAsJsonAsync($"api/planner-task/{plannerTaskId}/status", new { Status = status }, JsonOpts, CancellationToken);
            r.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        async Task<TodoListItem> LoadItemAsync()
        {
            await using var db = CreateDbContext();
            return await db.Set<TodoListItem>().SingleAsync(i => i.Id == todoItemId, CancellationToken);
        }

        await SetStatusAsync("Completed");
        var afterFirstDone = await LoadItemAsync();
        afterFirstDone.IsDone.Should().BeTrue();
        afterFirstDone.DoneCount.Should().Be(afterFirstDone.TotalCount);

        await SetStatusAsync("NotStarted");
        var afterUndone = await LoadItemAsync();
        afterUndone.IsDone.Should().BeFalse();
        afterUndone.DoneCount.Should().Be(0);

        await SetStatusAsync("Completed");
        var afterSecondDone = await LoadItemAsync();
        afterSecondDone.IsDone.Should().BeTrue();
        afterSecondDone.DoneCount.Should().Be(afterSecondDone.TotalCount);
    }

    [Fact(DisplayName = "Publishing the same planner-task event twice does not double-count the to-do item")]
    public async Task PublishingSameEventTwice_DoesNotDoubleCount()
    {
        long activityId, todoItemId;
        await using (var db = CreateDbContext())
        {
            var (aId, itemId, _) = await SeedLinkedTodoAndPlannerTaskAsync(db, "double-publish", totalCount: 3, stepCount: 3);
            activityId = aId;
            todoItemId = itemId;
        }

        var scopeFactory = Fixture.UnauthenticatedFactory.Services.GetRequiredService<IServiceScopeFactory>();
        var handler = new PlannerTaskIsDoneChangedEventHandler(scopeFactory, NullLogger<PlannerTaskIsDoneChangedEventHandler>.Instance);
        var eventModel = new PlannerTaskIsDoneChangedEvent(activityId, UserId, true, todoItemId);

        await handler.HandleAsync(eventModel, CancellationToken);
        await handler.HandleAsync(eventModel, CancellationToken);

        await using var db2 = CreateDbContext();
        var item = await db2.Set<TodoListItem>().SingleAsync(i => i.Id == todoItemId, CancellationToken);
        item.DoneCount.Should().Be(item.TotalCount, "SetDone assigns absolutely rather than incrementing, so replaying the same event must not push DoneCount past TotalCount");
        item.IsDone.Should().BeTrue();
    }

    /// <summary>
    /// Note on the UTC-midnight boundary the prompt asks for: the fixture's test user (and
    /// <see cref="ReminderSeedHelper.OtherUserId"/>) are both seeded with <c>Timezone = TimeZoneInfo.Utc</c>
    /// (see <c>AppDbContextFixture.SeedFixtureAsync</c> / <c>ReminderSeedHelper.EnsureOtherUserAsync</c>),
    /// and both the handler and <c>PatchPlannerTaskStatusEndpoint</c> derive "today" from
    /// <c>DateOnly.FromDateTime(DateTime.UtcNow)</c> directly rather than a user-local conversion. There is
    /// no non-UTC test user available to construct the boundary case the prompt describes without adding
    /// one — recorded here as a finding rather than bent into an assertion: "today" for this fan-out is
    /// UTC-today for every user, regardless of their <c>Timezone</c> setting, which is itself worth a
    /// follow-up if the product intent is user-local day boundaries.
    /// </summary>
    [Fact(DisplayName = "Only today's planner tasks flip when the to-do item is toggled — yesterday's is untouched")]
    public async Task TogglingTodoItem_OnlyFlipsTodaysPlannerTask()
    {
        long todoItemId, todayTaskId, yesterdayTaskId;
        await using (var db = CreateDbContext())
        {
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "only-today", UserId, CancellationToken);

            var item = new TodoListItem { UserId = UserId, ActivityId = activityId, TaskPriorityId = _taskPriorityId };
            db.Set<TodoListItem>().Add(item);
            await db.SaveChangesAsync(CancellationToken);

            var todayCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, Today, UserId, CancellationToken);
            var yesterdayCalendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, Today.AddDays(-1), UserId, CancellationToken);

            todayTaskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(
                db, activityId, todayCalendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), UserId, ct: CancellationToken);
            yesterdayTaskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(
                db, activityId, yesterdayCalendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), UserId, ct: CancellationToken);

            var todayTask = await db.Set<PlannerTask>().SingleAsync(t => t.Id == todayTaskId, CancellationToken);
            todayTask.TodolistItemId = item.Id;
            var yesterdayTask = await db.Set<PlannerTask>().SingleAsync(t => t.Id == yesterdayTaskId, CancellationToken);
            yesterdayTask.TodolistItemId = item.Id;
            await db.SaveChangesAsync(CancellationToken);

            todoItemId = item.Id;
        }

        var toggle = await ToggleTodoItemAsync(todoItemId, true);
        toggle.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db2 = CreateDbContext();
        (await db2.Set<PlannerTask>().SingleAsync(t => t.Id == todayTaskId, CancellationToken))
            .Status.Should().Be(PlannerTaskStatus.Completed);
        (await db2.Set<PlannerTask>().SingleAsync(t => t.Id == yesterdayTaskId, CancellationToken))
            .Status.Should().Be(PlannerTaskStatus.NotStarted, "only today's linked planner task should flip");
    }

    // ---- D: CQ-8 --------------------------------------------------------------------------------------
    //
    // Not implemented. ActivityAddedToHistoryEventHandler / ActivityCreatedIsOnToDoListEventHandler and
    // their events do not exist anywhere in this repo (host or framework/ submodule) — a repo-wide search
    // finds nothing, and current docs/domain-map.md says both were "removed as dead code — no publisher
    // ever existed for either." Activity also carries no is-on-to-do-list-style flag to drive the
    // create-with-auto-todo-item scenario the prompt describes. There is no code left to write a
    // regression test against; if that automation is still wanted, it needs to be designed and built
    // first; if not, the domain-map entry is already accurate and nothing further is owed here.

    // ---- E: SEC-8 -------------------------------------------------------------------------------------

    [Fact(DisplayName = "SEC-8: a planner-task completion event cannot flip another user's to-do item, even naming its id directly")]
    public async Task PlannerTaskEvent_CannotTouchAnotherUsersTodoItem()
    {
        long attackerActivityId, victimItemId;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);

            attackerActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "attacker-activity", UserId, CancellationToken);

            var victimPriority = new TaskPriority { UserId = ReminderSeedHelper.OtherUserId, Text = "Victim", Color = "#000000", Priority = 1 };
            db.Set<TaskPriority>().Add(victimPriority);
            await db.SaveChangesAsync(CancellationToken);

            var victimActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "victim-activity", ReminderSeedHelper.OtherUserId, CancellationToken);
            var victimItem = new TodoListItem
            {
                UserId = ReminderSeedHelper.OtherUserId,
                ActivityId = victimActivityId,
                TaskPriorityId = victimPriority.Id
            };
            db.Set<TodoListItem>().Add(victimItem);
            await db.SaveChangesAsync(CancellationToken);
            victimItemId = victimItem.Id;
        }

        // The real endpoint (PatchPlannerTaskStatusEndpoint) always builds this event from the acting
        // user's own entity, so HTTP cannot forge the id collision below. Publishing the event directly
        // against the handler — as ActivityTimeRecordedEventHandler's own tests do, since FastEndpoints
        // keeps its handler registry outside DI — is what the prompt itself suggests for this scenario.
        // The attacker's own UserId travels on the event exactly as a real publish would; only the
        // TodoListItemId is the victim's. SyncTodoListItem's `i.UserId == eventModel.UserId` predicate is
        // what has to hold for this to fail harmlessly.
        var scopeFactory = Fixture.UnauthenticatedFactory.Services.GetRequiredService<IServiceScopeFactory>();
        var handler = new PlannerTaskIsDoneChangedEventHandler(scopeFactory, NullLogger<PlannerTaskIsDoneChangedEventHandler>.Instance);
        await handler.HandleAsync(new PlannerTaskIsDoneChangedEvent(attackerActivityId, UserId, true, victimItemId), CancellationToken);

        await using var verifyDb = CreateDbContext();
        var victim = await verifyDb.Set<TodoListItem>().IgnoreQueryFilters().SingleAsync(i => i.Id == victimItemId, CancellationToken);
        victim.IsDone.Should().BeFalse("the handler's UserId predicate must keep one user's fan-out from touching another user's item");
    }

    // ---- F: concurrency ---------------------------------------------------------------------------------

    [Trait("Status", "KnownGap")]
    [Fact(DisplayName = "F: concurrent status patches on the same planner task should not surface as an unhandled 500")]
    public async Task ConcurrentStatusPatches_ShouldNotReturnInternalServerError()
    {
        long plannerTaskId;
        await using (var db = CreateDbContext())
        {
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "concurrency", UserId, CancellationToken);
            var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, Today, UserId, CancellationToken);
            plannerTaskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(
                db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), UserId, ct: CancellationToken);
        }

        var clientA = CreateUserRoleClient();
        var clientB = CreateUserRoleClient();

        // Both requests load the same PlannerTask row (which carries an EF-managed `row_version`
        // concurrency token — see EntityBuilderExtensions) into their own DbContext before either saves,
        // so whichever saves second hits a DbUpdateConcurrencyException on SaveChangesAsync. Neither this
        // handler chain nor PatchPlannerTaskStatusEndpoint itself special-cases that exception type — the
        // endpoint's blanket `catch (Exception ex) { ThrowError(ex.Message, 500); }` remaps it to a plain
        // 500, indistinguishable from a real server fault.
        var callA = clientA.PatchAsJsonAsync($"api/planner-task/{plannerTaskId}/status", new { Status = "Completed" }, JsonOpts, CancellationToken);
        var callB = clientB.PatchAsJsonAsync($"api/planner-task/{plannerTaskId}/status", new { Status = "Cancelled" }, JsonOpts, CancellationToken);
        var responses = await Task.WhenAll(callA, callB);

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.NoContent || r.StatusCode == HttpStatusCode.Conflict,
            "a benign optimistic-concurrency race between two writers to the same planner task should resolve as a clean last write or an explicit 409, never as an unhandled 500");
    }

    // ---- shared seeding helpers ---------------------------------------------------------------------

    /// <summary>
    /// An Activity with a TodoListItem and a same-day PlannerTask both pointing at it — the shape every
    /// scenario in this file starts from. <paramref name="totalCount"/>/<paramref name="stepCount"/> only
    /// matter for the step-counted scenarios; leave them null/0 for the plain done/not-done ones.
    /// </summary>
    private async Task<(long ActivityId, long TodoItemId, long PlannerTaskId)> SeedLinkedTodoAndPlannerTaskAsync(
        DbContext db,
        string name,
        int? totalCount = null,
        int stepCount = 0,
        PlannerTaskStatus taskStatus = PlannerTaskStatus.NotStarted,
        DateOnly? date = null,
        long userId = UserId)
    {
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, name, userId, CancellationToken);
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, date ?? Today, userId, CancellationToken);

        var item = new TodoListItem
        {
            UserId = userId,
            ActivityId = activityId,
            TaskPriorityId = _taskPriorityId,
            TotalCount = totalCount,
            DoneCount = totalCount.HasValue ? 0 : null
        };
        for (var i = 0; i < stepCount; i++)
            item.Steps.Add(new TodoListStep { Name = $"step-{i}" });
        db.Set<TodoListItem>().Add(item);
        await db.SaveChangesAsync(CancellationToken);

        var taskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(
            db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), userId, taskStatus, ct: CancellationToken);
        var task = await db.Set<PlannerTask>().SingleAsync(t => t.Id == taskId, CancellationToken);
        task.TodolistItemId = item.Id;
        await db.SaveChangesAsync(CancellationToken);

        return (activityId, item.Id, taskId);
    }

    private Task<HttpResponseMessage> ToggleTodoItemAsync(long todoItemId, bool forceValue) =>
        CreateClient().PatchAsJsonAsync("api/todo-list-item/toggle-is-done",
            new { Ids = new[] { todoItemId }, ForceValue = forceValue }, JsonOpts, CancellationToken);
}
