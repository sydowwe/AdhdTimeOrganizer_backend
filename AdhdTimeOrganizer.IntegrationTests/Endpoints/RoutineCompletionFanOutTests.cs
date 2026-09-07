using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.Planning.application.eventHandler;
using AdhdTimeOrganizer.Planning.application.service.reminder;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sydowwe.Framework.Testing;
using Sydowwe.Reminders.domain.entity;
using Sydowwe.Reminders.domain.@enum;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-21 — <c>RoutineTodoListIsDoneChangedEventHandler</c>, the Planning-side reaction to a routine
/// to-do list being ticked. It had no coverage at all: <c>CompletionFanOutTests</c> names it in its class
/// doc but every one of its methods drives one of the other two handlers (that was <c>DOC-5</c>), and
/// <c>Routines/RoutineToggleIsDoneTests.Toggle_PublishesRoutineTodoListIsDoneChangedEvent</c> asserts only
/// that the event leaves the endpoint, not what the handler does with it.
/// </summary>
/// <remarks>
/// Written alongside the fixes for the four findings the gap was hiding — <c>CQ-1</c> (assigned
/// <c>Status</c> directly instead of <c>PlannerTask.ApplyStatus</c>, so stale actual times survived),
/// <c>CQ-2</c> (never called <c>SyncForPlannerTasksAsync</c>, so a finished task kept its reminder),
/// <c>CQ-3</c> (no try/catch, so a Planning failure 500'd the Routines toggle) and <c>CQ-4</c> (overwrote
/// <c>Cancelled</c> tasks, which its sibling deliberately does not). Every test below is green against the
/// fixed handler; B, C, D and E were each red before it.
/// <para>
/// Two notes on scope, so the next reader does not go looking for tests that are deliberately absent:
/// </para>
/// <list type="bullet">
/// <item><b><c>CQ-5</c> ("today" is UTC here and user-local in both read paths) is not fixed and not
/// tested.</b> It is a three-call-site change spanning this handler, the to-do sibling and
/// <c>PatchPlannerTaskStatusEndpoint</c>, and it would have to move
/// <c>CompletionFanOutTests.TogglingTodoItem_OnlyFlipsTodaysPlannerTask</c> with it. Scenario A below pins
/// the current UTC reading the same way that test does, so whoever takes <c>CQ-5</c> has both pins in one
/// place to flip.</item>
/// <item><b>Scenario E uses an injected failure rather than a concurrency race.</b> The prompt suggests
/// racing a <c>PATCH /api/planner-task/{id}/status</c> against the handler's own save, which is what
/// <c>CompletionFanOutTests.ConcurrentStatusPatches_ShouldNotReturnInternalServerError</c> does — but as a
/// pin on <i>this</i> handler it only fails when the race lands the right way round, and it cannot
/// distinguish "the catch worked" from "the race did not happen". Throwing from the seam the handler
/// resolves inside its own try block makes the assertion deterministic and still exercises exactly the
/// path that matters: a Planning-side exception must not reach the Routines publisher.</item>
/// </list>
/// </remarks>
[Collection("Postgres")]
public class RoutineCompletionFanOutTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private static long OtherUserId => ReminderSeedHelper.OtherUserId;
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    // ---- A: the happy path, and the day boundary ----------------------------------------------------

    [Fact(DisplayName = "Ticking the routine list completes today's planner task for the same activity, and leaves yesterday's alone")]
    public async Task TickingRoutineList_OnlyFlipsTodaysPlannerTask()
    {
        long routineItemId, todayTaskId, yesterdayTaskId;
        await using (var db = CreateDbContext())
        {
            var (item, activityId) = await SeedRoutineItemAsync(db, "fanout-today");
            routineItemId = item.Id;

            todayTaskId = await SeedTaskOnAsync(db, activityId, Today);
            yesterdayTaskId = await SeedTaskOnAsync(db, activityId, Today.AddDays(-1));
        }

        (await ToggleRoutineAsync(routineItemId, forceValue: true)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db2 = CreateDbContext();
        (await LoadTaskAsync(db2, todayTaskId)).Status.Should().Be(PlannerTaskStatus.Completed);
        (await LoadTaskAsync(db2, yesterdayTaskId)).Status.Should().Be(PlannerTaskStatus.NotStarted,
            "the fan-out is scoped to the calendar row for today — CQ-5: 'today' is UTC-today here, not the user's own");
    }

    // ---- B: CQ-1 — the shared status reset ------------------------------------------------------------

    [Fact(DisplayName = "CQ-1: un-ticking the routine list clears the planner task's actual times, not just its status")]
    public async Task UntickingRoutineList_ClearsPlannerTaskActualTimes()
    {
        long routineItemId, taskId;
        await using (var db = CreateDbContext())
        {
            // Seeded done, so the toggle below is a genuine un-tick and the event carries NewIsDone = false.
            var (item, activityId) = await SeedRoutineItemAsync(db, "fanout-actualtimes", isDone: true);
            routineItemId = item.Id;

            taskId = await SeedTaskOnAsync(db, activityId, Today, PlannerTaskStatus.InProgress);
            var task = await LoadTaskAsync(db, taskId);
            task.ActualStartTime = new TimeOnly(9, 5);
            await db.SaveChangesAsync(CancellationToken);
        }

        (await ToggleRoutineAsync(routineItemId, forceValue: false)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db2 = CreateDbContext();
        var stored = await LoadTaskAsync(db2, taskId);
        stored.Status.Should().Be(PlannerTaskStatus.NotStarted);
        stored.ActualStartTime.Should().BeNull(
            "the handler must go through PlannerTask.ApplyStatus — a bare Status assignment leaves an actual time on a NotStarted row, which the plan-vs-actual trend then measures");
        stored.ActualEndTime.Should().BeNull();
    }

    // ---- C: CQ-2 — the reminder has to follow ----------------------------------------------------------

    [Fact(DisplayName = "CQ-2: ticking the routine list retires the reminder on the planner task it completes")]
    public async Task TickingRoutineList_RetiresLinkedPlannerTaskReminder()
    {
        long routineItemId, taskId;
        await using (var db = CreateDbContext())
        {
            var (item, activityId) = await SeedRoutineItemAsync(db, "fanout-reminder");
            routineItemId = item.Id;
            taskId = await SeedTaskOnAsync(db, activityId, Today);
        }

        // Created through the endpoint rather than seeded, so the module-side ReminderDefinition this
        // asserts on is registered the way production registers it.
        var created = await CreateClient().PostAsJsonAsync("api/reminder", new
        {
            Title = "Stretch",
            RemindAt = DateTime.UtcNow.AddHours(2),
            LeadOffsetsMinutes = new[] { 0 },
            PlannerTaskId = taskId
        }, JsonOpts, CancellationToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var reminderId = await created.Content.ReadFromJsonAsync<long>(CancellationToken);

        (await ToggleRoutineAsync(routineItemId, forceValue: true)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db2 = CreateDbContext();
        var definition = await db2.Set<ReminderDefinition>()
            .Where(d => d.OwnerModule == ReminderRegistrationService.OwnerModule
                        && d.SubjectType == ReminderRegistrationService.SubjectType
                        && d.SubjectId == reminderId.ToString()
                        && d.Kind == ReminderRegistrationService.Kind)
            .SingleAsync(CancellationToken);

        definition.Status.Should().Be(ReminderStatus.Cancelled,
            "finishing the planner task through the routine fan-out must retire its reminder, the same as a direct status patch or the to-do fan-out does");
    }

    // ---- D: CQ-4 — a cancelled task is the user's decision ---------------------------------------------

    [Fact(DisplayName = "CQ-4: ticking the routine list leaves an already-Cancelled planner task alone, skip reason intact")]
    public async Task TickingRoutineList_LeavesCancelledPlannerTaskAlone()
    {
        long routineItemId, cancelledTaskId, openTaskId;
        await using (var db = CreateDbContext())
        {
            var (item, activityId) = await SeedRoutineItemAsync(db, "fanout-cancelled");
            routineItemId = item.Id;

            cancelledTaskId = await SeedTaskOnAsync(db, activityId, Today, PlannerTaskStatus.Cancelled);
            var cancelled = await LoadTaskAsync(db, cancelledTaskId);
            cancelled.SkipReason = "Injured";
            await db.SaveChangesAsync(CancellationToken);

            // A second, ordinary task on the same activity and day: the skip must be surgical, not a
            // whole-batch bail-out that also drops the task the user does expect to move.
            openTaskId = await SeedTaskOnAsync(db, activityId, Today, PlannerTaskStatus.NotStarted, new TimeOnly(11, 0));
        }

        (await ToggleRoutineAsync(routineItemId, forceValue: true)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db2 = CreateDbContext();
        var cancelledTask = await LoadTaskAsync(db2, cancelledTaskId);
        cancelledTask.Status.Should().Be(PlannerTaskStatus.Cancelled,
            "a task the user deliberately cancelled must not be silently un-cancelled or re-completed by the routine toggle");
        cancelledTask.SkipReason.Should().Be("Injured", "an orphaned skip reason on a completed row is the visible half of the same bug");

        (await LoadTaskAsync(db2, openTaskId)).Status.Should().Be(PlannerTaskStatus.Completed,
            "only the cancelled task is skipped; its neighbour still follows the toggle");
    }

    // ---- E: CQ-3 — a Planning failure is not the publisher's problem -----------------------------------

    [Fact(DisplayName = "CQ-3: a failure inside the Planning handler does not surface as a 500 on the Routines toggle")]
    public async Task HandlerFailure_DoesNotFailTheRoutineToggle()
    {
        long routineItemId;
        await using (var db = CreateDbContext())
        {
            var (item, activityId) = await SeedRoutineItemAsync(db, "fanout-throws");
            routineItemId = item.Id;
            await SeedTaskOnAsync(db, activityId, Today);
        }

        // The Routines toggle endpoint never touches this seam; only the Planning handler resolves it, and
        // it does so inside the try block, so this is a clean stand-in for the DbUpdateConcurrencyException
        // a concurrent status patch would raise there.
        var reminders = new Mock<IReminderRegistrationService>();
        reminders
            .Setup(r => r.SyncForPlannerTasksAsync(It.IsAny<IReadOnlyCollection<long>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException("another writer won the row_version race"));

        await using var factory = CreateFactory(["User"], configureServices: services =>
        {
            services.RemoveAll<IReminderRegistrationService>();
            services.AddScoped(_ => reminders.Object);
        });

        var response = await ToggleRoutineAsync(routineItemId, forceValue: true, client: factory.CreateClient());

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "the toggle is awaited via PublishAsync(Mode.WaitForAll, …) after its own commit, so a Planning-side throw would 500 an endpoint that already succeeded");

        await using var db2 = CreateDbContext();
        var storedItem = await db2.Set<RoutineTodoList>().AsNoTracking()
            .SingleAsync(i => i.Id == routineItemId, CancellationToken);
        storedItem.IsDone.Should().BeTrue("the routine's own committed state must survive the swallowed failure, not be reported back as un-toggled");
    }

    // ---- G: isolation ------------------------------------------------------------------------------------

    [Fact(DisplayName = "A routine fan-out event naming another user's activity cannot move that user's planner task")]
    public async Task RoutineEvent_CannotTouchAnotherUsersPlannerTask()
    {
        long victimActivityId, victimTaskId;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            victimActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "victim-routine-activity", OtherUserId, CancellationToken);
            victimTaskId = await SeedTaskOnAsync(db, victimActivityId, Today, userId: OtherUserId);
        }

        // HTTP cannot forge this: RoutineToggleIsDoneTodoListEndpoint always builds the event from the
        // acting user's own RoutineTodoList. Publishing straight at the handler — as
        // CompletionFanOutTests.PlannerTaskEvent_CannotTouchAnotherUsersTodoItem does for the host-side
        // handler — is the only way to reach the predicate under test. The attacker's own UserId travels
        // on the event exactly as a real publish would; only the ActivityId is the victim's.
        //
        // Worth pinning precisely because there is no safety net underneath it: the handler runs in a fresh
        // scope with no authenticated principal, so AppDbContext's ScopeUserId is null and the global
        // IEntityWithUser filter is a no-op. The explicit `pt.UserId == eventModel.UserId` predicate is the
        // whole protection — and its absence in the to-do sibling is SEC-2.
        var scopeFactory = Fixture.UnauthenticatedFactory.Services.GetRequiredService<IServiceScopeFactory>();
        var handler = new RoutineTodoListIsDoneChangedEventHandler(
            scopeFactory, NullLogger<RoutineTodoListIsDoneChangedEventHandler>.Instance);

        await handler.HandleAsync(new RoutineTodoListIsDoneChangedEvent(victimActivityId, UserId, true), CancellationToken);

        await using var verify = CreateDbContext();
        var victimTask = await verify.Set<PlannerTask>().AsNoTracking().IgnoreQueryFilters()
            .SingleAsync(t => t.Id == victimTaskId, CancellationToken);
        victimTask.Status.Should().Be(PlannerTaskStatus.NotStarted,
            "the handler's UserId predicate must keep one user's routine fan-out from touching another user's planner task");
    }

    // ---- seeding helpers ----------------------------------------------------------------------------------

    /// <summary>
    /// A routine to-do list on a period that is <b>not</b> due — <c>LastResetAt = now</c> with a rolling
    /// anchor — so nothing in <c>FetchAndPrepare</c>'s lazy-reset path fires and the only thing these tests
    /// observe is the toggle and its fan-out. Text and length are unique per call because
    /// <c>routine_time_period</c> has unique indexes on <c>(user_id, text)</c> and
    /// <c>(user_id, length_in_days)</c>; the activity is returned because the handler joins on it.
    /// </summary>
    private async Task<(RoutineTodoList Item, long ActivityId)> SeedRoutineItemAsync(
        DbContext db, string label, bool isDone = false, long userId = UserId)
    {
        var period = new RoutineTimePeriod
        {
            UserId = userId,
            Text = $"Period {Guid.NewGuid():N}",
            Color = "#123456",
            LengthInDays = 7,
            ResetAnchorDay = 0,
            StreakThreshold = 100,
            StreakGraceDays = 0,
            Streak = 0,
            BestStreak = 0,
            LastResetAt = DateTime.UtcNow
        };
        db.Set<RoutineTimePeriod>().Add(period);
        await db.SaveChangesAsync(CancellationToken);

        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"{label}-{Guid.NewGuid():N}", userId, CancellationToken);

        var item = new RoutineTodoList
        {
            UserId = userId,
            ActivityId = activityId,
            TimePeriodId = period.Id,
            IsDone = isDone,
            LastResetDate = Today,
            DisplayOrder = 1000
        };
        db.Set<RoutineTodoList>().Add(item);
        await db.SaveChangesAsync(CancellationToken);
        return (item, activityId);
    }

    private async Task<long> SeedTaskOnAsync(
        DbContext db,
        long activityId,
        DateOnly date,
        PlannerTaskStatus status = PlannerTaskStatus.NotStarted,
        TimeOnly? start = null,
        long userId = UserId)
    {
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, date, userId, CancellationToken);
        var from = start ?? new TimeOnly(9, 0);
        return await PlanningTestSeedHelper.SeedPlannerTaskAsync(
            db, activityId, calendarId, from, from.AddHours(1), userId, status, ct: CancellationToken);
    }

    private Task<PlannerTask> LoadTaskAsync(DbContext db, long taskId) =>
        db.Set<PlannerTask>().SingleAsync(t => t.Id == taskId, CancellationToken);

    private Task<HttpResponseMessage> ToggleRoutineAsync(long itemId, bool? forceValue = null, HttpClient? client = null) =>
        (client ?? CreateClient()).PatchAsJsonAsync(
            "api/routine-todo-list/toggle-is-done",
            new { Ids = new[] { itemId }, ForceValue = forceValue },
            JsonOpts,
            CancellationToken);
}
