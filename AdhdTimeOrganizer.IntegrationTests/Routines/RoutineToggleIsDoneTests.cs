using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.application.eventHandler;
using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.IntegrationTests.Endpoints;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.model.@enum;
using AdhdTimeOrganizer.Routines.domain.serviceContract;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Routines;

/// <summary>
/// TEST-8 / TEST-9 — the two routine toggle routes, which had no coverage at all despite being the densest
/// endpoints in the module:
/// <list type="bullet">
/// <item><c>PATCH /api/routine-todo-list/toggle-is-done</c> (<c>RoutineToggleIsDoneTodoListEndpoint</c>)</item>
/// <item><c>PATCH /api/routine-todo-list/{itemId}/steps/{stepId}/toggle</c>
/// (<c>ToggleStepIsDoneRoutineTodoListEndpoint</c>)</item>
/// </list>
/// <c>Endpoints/CompletionFanOutTests</c> drives <c>api/todo-list-item/toggle-is-done</c> — the TodoLists
/// sibling — so nothing there covers either of these.
/// </summary>
/// <remarks>
/// Two points where the originating prompt (<c>review/routines/testingPrompts/ToggleIsDoneStreak.md</c>) is
/// stale against current source, reconfirmed by reading the files rather than trusting the prompt:
/// <list type="bullet">
/// <item><b>CQ-7 is already fixed.</b> The prompt quotes a <c>RoutineResetService.UpdateItemStreak</c> that
/// increments unconditionally and calls both inflation tests "fails today". The current implementation
/// (<c>:344-376</c>) gates the increment on <c>AlreadyCountedThisCycle</c>, and the two pure-domain tests the
/// prompt asks for already exist in <c>Services/RoutineResetServiceTests.cs</c>
/// (<c>UpdateItemStreak_CalledTwiceInTheSameCycle_IncrementsOnlyOnce</c> and
/// <c>UpdateItemStreak_AfterANewCycle_IncrementsAgain</c>). The §1 tests below are therefore written as
/// green endpoint-level pins over that fix, not as expected-failing ones.</item>
/// <item><b>The step toggle did not reach <c>UpdateItemStreak</c>.</b> Routines overrides the item-done hook
/// (<c>ToggleStepIsDoneRoutineTodoListEndpoint.OnItemDoneChanged</c>, then named <c>OnItemCompleted</c>), but
/// the only call to it was commented out in <c>BaseToggleStepIsDoneEndpoint.ApplyStepToggle</c>, so completing
/// an item by its last step scored no streak at all. Found by
/// <see cref="StepToggle_CompletingTheLastStep_UpdatesTheItemStreak"/> and fixed by restoring that call; the
/// test now stands as its regression pin.</item>
/// </list>
/// </remarks>
[Collection("Postgres")]
public class RoutineToggleIsDoneTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private static long OtherUserId => ReminderSeedHelper.OtherUserId;
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    // ---- seeding helpers ---------------------------------------------------------------------------

    /// <summary>
    /// A period with <c>ResetAnchorDay = 0</c> (rolling), which is the only anchor that is due exactly
    /// <c>LengthInDays</c> after <paramref name="lastResetAt"/> on every calendar day — the tests below choose
    /// "elapsed" or "current" purely by that offset. Text and length are unique per call because
    /// <c>routine_time_period</c> has unique indexes on <c>(user_id, text)</c> <b>and</b>
    /// <c>(user_id, length_in_days)</c>.
    /// </summary>
    private async Task<RoutineTimePeriod> SeedPeriodAsync(
        DbContext db,
        DateTime lastResetAt,
        long userId = UserId,
        int lengthInDays = 7,
        int streakThreshold = 100,
        int streakGraceDays = 0,
        int streak = 0)
    {
        var period = new RoutineTimePeriod
        {
            UserId = userId,
            Text = $"Period {Guid.NewGuid():N}",
            Color = "#123456",
            LengthInDays = lengthInDays,
            ResetAnchorDay = 0,
            StreakThreshold = streakThreshold,
            StreakGraceDays = streakGraceDays,
            Streak = streak,
            BestStreak = streak,
            LastResetAt = lastResetAt
        };
        db.Set<RoutineTimePeriod>().Add(period);
        await db.SaveChangesAsync(CancellationToken);
        return period;
    }

    private async Task<RoutineTodoList> SeedItemAsync(
        DbContext db,
        RoutineTimePeriod period,
        string label,
        long userId = UserId,
        bool isDone = false,
        int? totalCount = null,
        int? doneCount = null,
        int streak = 0,
        DateOnly? lastResetDate = null,
        int stepCount = 0)
    {
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"{label}-{Guid.NewGuid():N}", userId, CancellationToken);

        var item = new RoutineTodoList
        {
            UserId = userId,
            ActivityId = activityId,
            TimePeriodId = period.Id,
            IsDone = isDone,
            TotalCount = totalCount,
            DoneCount = doneCount,
            Streak = streak,
            BestStreak = streak,
            LastResetDate = lastResetDate,
            DisplayOrder = 1000
        };
        for (var i = 0; i < stepCount; i++)
            item.Steps.Add(new TodoListStep { Name = $"{label}-step-{i}", Order = i, IsDone = isDone });

        db.Set<RoutineTodoList>().Add(item);
        await db.SaveChangesAsync(CancellationToken);
        return item;
    }

    private Task<HttpResponseMessage> ToggleAsync(long itemId, bool? forceValue = null, HttpClient? client = null) =>
        (client ?? CreateClient()).PatchAsJsonAsync(
            "api/routine-todo-list/toggle-is-done",
            new { Ids = new[] { itemId }, ForceValue = forceValue },
            JsonOpts,
            CancellationToken);

    private Task<HttpResponseMessage> ToggleStepAsync(long itemId, Guid stepId, HttpClient? client = null) =>
        (client ?? CreateClient()).PatchAsync(
            $"api/routine-todo-list/{itemId}/steps/{stepId}/toggle",
            new StringContent(string.Empty),
            CancellationToken);

    /// <summary>Reads through a fresh context on purpose — a graph still tracked from seeding would mask a
    /// query shape that forgets an <c>.Include</c>, and the toggle's writes all have to be on the row.</summary>
    private async Task<RoutineTodoList> LoadItemAsync(long itemId)
    {
        await using var db = CreateDbContext();
        return await db.Set<RoutineTodoList>().AsNoTracking()
            .Include(i => i.Steps)
            .FirstAsync(i => i.Id == itemId, CancellationToken);
    }

    private async Task<RoutineTimePeriod> LoadPeriodAsync(long periodId)
    {
        await using var db = CreateDbContext();
        return await db.Set<RoutineTimePeriod>().AsNoTracking()
            .FirstAsync(p => p.Id == periodId, CancellationToken);
    }

    private async Task<List<RoutinePeriodCompletion>> LoadCompletionsAsync(long periodId)
    {
        await using var db = CreateDbContext();
        return await db.Set<RoutinePeriodCompletion>().AsNoTracking()
            .Where(c => c.TimePeriodId == periodId)
            .ToListAsync(CancellationToken);
    }

    // ---- §1 streak inflation (CQ-7) -------------------------------------------------------------------

    /// <summary>
    /// The first of the two routes into streak inflation: un-tick and re-tick inside one cycle. Before the
    /// <c>AlreadyCountedThisCycle</c> guard a user could sit and watch a daily routine's streak climb, since
    /// nothing keyed the increment to the cycle.
    /// </summary>
    [Fact]
    public async Task Toggle_UntickThenRetick_DoesNotInflateTheItemStreak()
    {
        long itemId;
        await using (var db = CreateDbContext())
        {
            // Just reset, so nothing about this period is due — the only thing moving is the toggle itself.
            var period = await SeedPeriodAsync(db, lastResetAt: DateTime.UtcNow);
            itemId = (await SeedItemAsync(db, period, "untick-retick", lastResetDate: Today)).Id;
        }

        (await ToggleAsync(itemId)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ToggleAsync(itemId)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ToggleAsync(itemId)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var stored = await LoadItemAsync(itemId);
        stored.IsDone.Should().BeTrue();
        stored.Streak.Should().Be(1, "the increment is once per cycle, not once per completing toggle");
        stored.BestStreak.Should().Be(1);
    }

    /// <summary>
    /// The second route in, and the one a fix keyed only on "was it un-ticked" would miss: no un-tick is
    /// needed at all. <c>IsDoneLogic</c> with <c>forceValue: true</c> clamps <c>DoneCount</c> to
    /// <c>TotalCount</c> and leaves <c>IsDone</c> true (<c>BaseToggleIsDoneTodoListEndpoint:91-101</c>), but
    /// the base still runs <c>AfterItemToggled</c> on every request (<c>:40</c>) — so re-sending the same
    /// force-true at an already-complete counted item reaches <c>UpdateItemStreak</c> each time.
    /// </summary>
    [Fact]
    public async Task Toggle_ForceTrueTwiceOnACompleteCountedItem_DoesNotInflateTheItemStreak()
    {
        long itemId;
        await using (var db = CreateDbContext())
        {
            var period = await SeedPeriodAsync(db, lastResetAt: DateTime.UtcNow);
            itemId = (await SeedItemAsync(db, period, "force-true", totalCount: 2, doneCount: 0, lastResetDate: Today)).Id;
        }

        // Force-true increments by one per call, so the second call is the one that completes the item;
        // the third is the repeat that must not score again.
        (await ToggleAsync(itemId, forceValue: true)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ToggleAsync(itemId, forceValue: true)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ToggleAsync(itemId, forceValue: true)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var stored = await LoadItemAsync(itemId);
        stored.IsDone.Should().BeTrue();
        stored.DoneCount.Should().Be(2, "force-true clamps at TotalCount rather than running past it");
        stored.Streak.Should().Be(1, "a repeated force-true at a complete item must not walk the counters up");
        stored.BestStreak.Should().Be(1);
    }

    [Fact]
    public async Task Toggle_CompletingAnItem_SetsLastCompletedAtAndStreak()
    {
        long itemId;
        await using (var db = CreateDbContext())
        {
            var period = await SeedPeriodAsync(db, lastResetAt: DateTime.UtcNow);
            itemId = (await SeedItemAsync(db, period, "happy-path", streak: 4, lastResetDate: Today)).Id;
        }

        var before = DateTime.UtcNow;
        (await ToggleAsync(itemId, forceValue: true)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var stored = await LoadItemAsync(itemId);
        stored.IsDone.Should().BeTrue();
        stored.Streak.Should().Be(5);
        stored.BestStreak.Should().Be(5);
        stored.LastCompletedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTime.UtcNow);
    }

    // ---- §1b the un-tick walk-back (ask B9) ------------------------------------------------------------

    /// <summary>
    /// The decision behind ask B9: an accidental tick undone inside the same cycle costs nothing. Before this
    /// the first tick in a cycle was <c>+1</c> permanently — the un-tick path returned before touching the
    /// counters — and the e2e fixture's "Meditation" item drifted from 5 to 6 on a single stray click. Seeded
    /// at <c>Streak == BestStreak</c> so the tick sets a record and the un-tick has both to give back.
    /// </summary>
    [Fact]
    public async Task Toggle_UntickInsideTheSameCycle_GivesTheStreakBack()
    {
        long itemId;
        await using (var db = CreateDbContext())
        {
            var period = await SeedPeriodAsync(db, lastResetAt: DateTime.UtcNow);
            itemId = (await SeedItemAsync(db, period, "untick-walkback", streak: 4, lastResetDate: Today)).Id;
        }

        (await ToggleAsync(itemId)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await LoadItemAsync(itemId)).Streak.Should().Be(5, "the tick scored");

        (await ToggleAsync(itemId)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var stored = await LoadItemAsync(itemId);
        stored.IsDone.Should().BeFalse();
        stored.Streak.Should().Be(4, "the un-tick inside the cycle the score belongs to gives the point back");
        stored.BestStreak.Should().Be(4, "and the record the tick set goes with it");
        stored.BestStreakBeforeScore.Should().BeNull("the item is open to score again");
    }

    /// <summary>
    /// The mirror of <see cref="Toggle_UntickInsideTheSameCycle_GivesTheStreakBack"/>: a score from a cycle that
    /// has since closed is beyond reach. The reset clears the mark, so once the item re-scores in the fresh
    /// cycle an un-tick returns exactly that one point — never the closed cycle's — and the walk-back bottoms
    /// out where the closed cycle left it.
    /// </summary>
    [Fact]
    public async Task Toggle_UntickAfterTheCycleClosed_LeavesTheEarlierScoreAlone()
    {
        long itemId;
        await using (var db = CreateDbContext())
        {
            var period = await SeedPeriodAsync(db, lastResetAt: DateTime.UtcNow.AddDays(-8), streak: 3);
            var item = await SeedItemAsync(db, period, "untick-old-cycle", isDone: true, streak: 4, lastResetDate: Today.AddDays(-8));
            // Ticked and scored — a record, 3 → 4 — in the cycle that is about to close.
            item.BestStreakBeforeScore = 3;
            await db.SaveChangesAsync(CancellationToken);
            itemId = item.Id;
        }

        // The reset runs first (the item was done, so it keeps its 4), then this tick scores the fresh cycle.
        (await ToggleAsync(itemId, forceValue: true)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await LoadItemAsync(itemId)).Streak.Should().Be(5);

        (await ToggleAsync(itemId, forceValue: false)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var stored = await LoadItemAsync(itemId);
        stored.Streak.Should().Be(4, "only the open cycle's point is returned; the closed cycle's stands");
        stored.BestStreak.Should().Be(4, "the record the undone tick set goes with it, down to the one the closed cycle set");

        // And a second un-tick has nothing left to give.
        (await ToggleAsync(itemId, forceValue: false)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await LoadItemAsync(itemId)).Streak.Should().Be(4, "the closed cycle's score is not the open cycle's to return");
    }

    /// <summary>
    /// The other half of the same decision: a cycle that closes with the item undone breaks the item's streak,
    /// as it always did the period's. Before this nothing ever zeroed <c>RoutineTodoList.Streak</c>, so an
    /// item's "streak" was a lifetime count and its <c>BestStreak</c> could never exceed it.
    /// </summary>
    [Fact]
    public async Task Toggle_OnAnElapsedPeriod_BreaksTheStreakOfAnItemLeftUndone()
    {
        long undoneItemId, doneItemId;
        await using (var db = CreateDbContext())
        {
            var period = await SeedPeriodAsync(db, lastResetAt: DateTime.UtcNow.AddDays(-8));
            undoneItemId = (await SeedItemAsync(db, period, "elapsed-undone", streak: 6, lastResetDate: Today.AddDays(-8))).Id;
            doneItemId = (await SeedItemAsync(db, period, "elapsed-done-kept", isDone: true, streak: 6, lastResetDate: Today.AddDays(-8))).Id;
        }

        // Toggling either item resets the whole period first; the done one is the toggled one so the undone
        // one is touched by nothing but the reset.
        (await ToggleAsync(doneItemId, forceValue: true)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var undone = await LoadItemAsync(undoneItemId);
        undone.Streak.Should().Be(0, "undone at the boundary is a miss, and a miss breaks the item's streak");
        undone.BestStreak.Should().Be(6, "the record survives the break");

        var done = await LoadItemAsync(doneItemId);
        done.Streak.Should().Be(7, "done at the boundary kept its 6, and the tick after the reset scored the new cycle");
    }

    // ---- §2 the reset side-effect --------------------------------------------------------------------

    /// <summary>
    /// <c>FetchAndPrepare</c> (<c>:29-81</c>) applies <c>CheckGrace</c> + the <b>list</b> <c>TryResetCatchUp</c>
    /// before anything is toggled, so a toggle can be the request that first notices an elapsed period. That
    /// makes this endpoint a documented reset site (<c>IRoutinePeriodNotificationService:18-22</c>) and it owes
    /// everything the nightly job owes: the completion row, the advanced <c>LastResetAt</c>, and the other
    /// items unticked — all of it before the toggled item's own change lands.
    /// </summary>
    [Fact]
    public async Task Toggle_OnAnElapsedPeriod_ResetsFirstThenApplies()
    {
        long periodId, doneItemId, toggledItemId;
        // 8 days against a 7-day rolling period: exactly one boundary has passed, so this closes one cycle.
        var lastResetAt = DateTime.UtcNow.AddDays(-8);
        await using (var db = CreateDbContext())
        {
            var period = await SeedPeriodAsync(db, lastResetAt: lastResetAt, streak: 3);
            periodId = period.Id;
            doneItemId = (await SeedItemAsync(db, period, "elapsed-done", isDone: true)).Id;
            toggledItemId = (await SeedItemAsync(db, period, "elapsed-toggled")).Id;
        }

        (await ToggleAsync(toggledItemId, forceValue: true)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var storedPeriod = await LoadPeriodAsync(periodId);
        storedPeriod.LastResetAt.Should().Be(lastResetAt.AddDays(7).Date,
            "the toggle closes the cycle that was due, landing LastResetAt on that boundary");
        storedPeriod.Streak.Should().Be(0, "one of two items was done against a 100% threshold with no grace");

        var completions = await LoadCompletionsAsync(periodId);
        completions.Should().ContainSingle();
        completions[0].CompletedCount.Should().Be(1);
        completions[0].TotalCount.Should().Be(2);

        var doneItem = await LoadItemAsync(doneItemId);
        doneItem.IsDone.Should().BeFalse("the reset unticks every item in the period, not just the toggled one");
        doneItem.LastResetDate.Should().Be(Today);

        var toggledItem = await LoadItemAsync(toggledItemId);
        toggledItem.IsDone.Should().BeTrue("the toggle applies on top of the reset it triggered");
        toggledItem.Streak.Should().Be(1, "the reset opened a fresh cycle, so this completion scores");
    }

    /// <summary>
    /// <c>AfterSave</c> (<c>:100-104</c>) exists so the end-of-period summary is announced only once the toggle
    /// is committed — a notification is about something that already happened and must not be able to roll it
    /// back. The probe below reads through a <b>separate connection</b> from inside the notifier, so it sees
    /// only committed rows: if the hook ever moves ahead of the save, that read comes back false.
    /// </summary>
    [Fact]
    public async Task Toggle_OnAnElapsedPeriod_RaisesThePeriodEndedSummary()
    {
        long toggledItemId;
        await using (var db = CreateDbContext())
        {
            var period = await SeedPeriodAsync(db, lastResetAt: DateTime.UtcNow.AddDays(-8), streak: 3);
            await SeedItemAsync(db, period, "notify-done", isDone: true);
            toggledItemId = (await SeedItemAsync(db, period, "notify-toggled")).Id;
        }

        var calls = 0;
        var outcome = StreakOutcome.NotEvaluated;
        var toggleWasCommittedAtNotifyTime = false;

        var notifier = new Mock<IRoutinePeriodNotificationService>();
        notifier
            .Setup(n => n.NotifyPeriodEndedAsync(
                It.IsAny<RoutineTimePeriod>(), It.IsAny<RoutinePeriodCompletion>(), It.IsAny<StreakOutcome>(), It.IsAny<CancellationToken>()))
            .Returns(async (RoutineTimePeriod _, RoutinePeriodCompletion _, StreakOutcome o, CancellationToken _) =>
            {
                calls++;
                outcome = o;
                await using var probe = CreateDbContext();
                toggleWasCommittedAtNotifyTime = await probe.Set<RoutineTodoList>().AsNoTracking()
                    .Where(i => i.Id == toggledItemId)
                    .Select(i => i.IsDone)
                    .FirstAsync(CancellationToken);
            });

        await using var factory = CreateFactory(["User"], configureServices: services =>
        {
            services.RemoveAll<IRoutinePeriodNotificationService>();
            services.AddScoped(_ => notifier.Object);
        });

        (await ToggleAsync(toggledItemId, forceValue: true, client: factory.CreateClient()))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        calls.Should().Be(1, "one closed cycle owes exactly one summary");
        outcome.Should().Be(StreakOutcome.Broken, "one of two items was done against a 100% threshold with no grace");
        toggleWasCommittedAtNotifyTime.Should().BeTrue("AfterSave runs after the commit, so the toggle is already durable");
    }

    // ---- §3 the cross-slice event ---------------------------------------------------------------------

    /// <summary>
    /// <c>PublishEvent</c> (<c>:106-110</c>) raises <c>RoutineTodoListIsDoneChangedEvent</c> — the module's only
    /// outbound event and the sole channel into Planning. <c>Endpoints/CompletionFanOutTests</c> covers the
    /// handler but drives it from <c>api/todo-list-item/toggle-is-done</c>, a different endpoint; this asserts
    /// the fan-out actually leaves <b>this</b> route.
    /// </summary>
    [Fact]
    public async Task Toggle_PublishesRoutineTodoListIsDoneChangedEvent()
    {
        long itemId, plannerTaskId;
        await using (var db = CreateDbContext())
        {
            var period = await SeedPeriodAsync(db, lastResetAt: DateTime.UtcNow);
            var item = await SeedItemAsync(db, period, "fan-out", lastResetDate: Today);
            itemId = item.Id;

            // The handler matches on (ActivityId, UserId, today's calendar), so the planner task has to hang
            // off the same activity as the routine item and sit on today's calendar to be reachable at all.
            var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, Today, UserId, CancellationToken);
            plannerTaskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(
                db, item.ActivityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), UserId, ct: CancellationToken);
        }

        (await ToggleAsync(itemId, forceValue: true)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var db = CreateDbContext())
        {
            var task = await db.Set<PlannerTask>().AsNoTracking().SingleAsync(t => t.Id == plannerTaskId, CancellationToken);
            task.Status.Should().Be(PlannerTaskStatus.Completed);
        }

        // The mirror direction, so the pin covers the event's payload and not just "something fired".
        (await ToggleAsync(itemId, forceValue: false)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var verify = CreateDbContext();
        var reopened = await verify.Set<PlannerTask>().AsNoTracking().SingleAsync(t => t.Id == plannerTaskId, CancellationToken);
        reopened.Status.Should().Be(PlannerTaskStatus.NotStarted);
    }

    /// <summary>
    /// The inbound direction — <c>PlannerTaskIsDoneChangedEventHandler</c> completing or reopening the routine
    /// item — is a write site like the two toggle routes, so it owes the same lazy single-item reset before it
    /// touches <c>IsDone</c>. Without it, a planner un-tick landing after the period elapsed but before any reset
    /// ran walked back a score that belonged to the closed cycle, and a planner tick scored a cycle the item had
    /// already missed. Drives the handler directly, the way <c>CompletionFanOutTests</c> does, so the pin does not
    /// depend on which planner endpoint raises the event.
    /// </summary>
    [Fact]
    public async Task PlannerHandler_OnAnElapsedPeriod_ResetsTheItemBeforeApplyingTheChange()
    {
        long untickedItemId, tickedItemId, untickedActivityId, tickedActivityId;
        await using (var db = CreateDbContext())
        {
            var period = await SeedPeriodAsync(db, lastResetAt: DateTime.UtcNow.AddDays(-8));

            // Done and scored (a record, 3 → 4) in the cycle that has since closed.
            var unticked = await SeedItemAsync(db, period, "planner-untick-elapsed", isDone: true, streak: 4, lastResetDate: Today.AddDays(-8));
            unticked.BestStreakBeforeScore = 3;
            await db.SaveChangesAsync(CancellationToken);
            (untickedItemId, untickedActivityId) = (unticked.Id, unticked.ActivityId);

            // Left undone in the cycle that has since closed.
            var ticked = await SeedItemAsync(db, period, "planner-tick-elapsed", streak: 6, lastResetDate: Today.AddDays(-8));
            (tickedItemId, tickedActivityId) = (ticked.Id, ticked.ActivityId);
        }

        var scopeFactory = Fixture.UnauthenticatedFactory.Services.GetRequiredService<IServiceScopeFactory>();
        var handler = new PlannerTaskIsDoneChangedEventHandler(scopeFactory, NullLogger<PlannerTaskIsDoneChangedEventHandler>.Instance);

        await handler.HandleAsync(new PlannerTaskIsDoneChangedEvent(untickedActivityId, UserId, false, null), CancellationToken);
        await handler.HandleAsync(new PlannerTaskIsDoneChangedEvent(tickedActivityId, UserId, true, null), CancellationToken);

        var unticked2 = await LoadItemAsync(untickedItemId);
        unticked2.IsDone.Should().BeFalse();
        unticked2.LastResetDate.Should().Be(Today, "the handler noticed the elapsed period and reset the item first");
        unticked2.Streak.Should().Be(4, "the reset closed the cycle with the item done, so its score stands and the un-tick has nothing to give back");
        unticked2.BestStreak.Should().Be(4, "the record the closed cycle set is not the open cycle's to return");
        unticked2.BestStreakBeforeScore.Should().BeNull();

        var ticked2 = await LoadItemAsync(tickedItemId);
        ticked2.IsDone.Should().BeTrue();
        ticked2.LastResetDate.Should().Be(Today);
        ticked2.Streak.Should().Be(1, "the reset broke the streak the item missed, and the tick scored the fresh cycle from zero");
        ticked2.BestStreak.Should().Be(6, "the record survives the break");
        ticked2.BestStreakBeforeScore.Should().Be(6, "the tick scored the open cycle, so an un-tick can still return it");
    }

    // ---- §4 (TEST-9) the step toggle -------------------------------------------------------------------

    [Fact]
    public async Task StepToggle_CompletingTheLastStep_MarksTheItemDone()
    {
        long itemId;
        Guid stepId;
        await using (var db = CreateDbContext())
        {
            var period = await SeedPeriodAsync(db, lastResetAt: DateTime.UtcNow);
            var item = await SeedItemAsync(db, period, "step-complete", lastResetDate: Today, stepCount: 1);
            itemId = item.Id;
            stepId = item.Steps.Single().Id;
        }

        (await ToggleStepAsync(itemId, stepId)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var stored = await LoadItemAsync(itemId);
        stored.IsDone.Should().BeTrue("the item has no TotalCount, so all-steps-done is the whole condition");
        stored.Steps.Should().ContainSingle().Which.IsDone.Should().BeTrue();
    }

    /// <summary>
    /// Completing an item by ticking its last step must score the item streak exactly like the bulk toggle
    /// does, and — since it shares <c>UpdateItemStreak</c> — must not score twice when the step is un-ticked
    /// and re-ticked inside the same cycle.
    /// <para>
    /// This is the regression pin for the bug it was written to find: Routines overrides the item-done hook
    /// (<c>ToggleStepIsDoneRoutineTodoListEndpoint.OnItemDoneChanged</c>), but the only call site
    /// was commented out in <c>BaseToggleStepIsDoneEndpoint.ApplyStepToggle</c>, so the hook never ran and a
    /// routine completed through its checklist scored nothing while the same item completed through the bulk
    /// toggle scored normally. Restoring that call is the whole fix. Note that
    /// <c>review/todo-lists/02-findings.md</c>'s CQ-11 proposes <i>deleting</i> the hook on the grounds that
    /// "no subclass overrides" it — which is not true of this solution, and is how the call came to be
    /// commented out in the first place.
    /// </para>
    /// </summary>
    [Fact]
    public async Task StepToggle_CompletingTheLastStep_UpdatesTheItemStreak()
    {
        long itemId;
        Guid stepId;
        await using (var db = CreateDbContext())
        {
            var period = await SeedPeriodAsync(db, lastResetAt: DateTime.UtcNow);
            var item = await SeedItemAsync(db, period, "step-streak", lastResetDate: Today, stepCount: 1);
            itemId = item.Id;
            stepId = item.Steps.Single().Id;
        }

        (await ToggleStepAsync(itemId, stepId)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterFirst = await LoadItemAsync(itemId);
        afterFirst.Streak.Should().Be(1, "completing an item by its last step is a completion like any other");

        // Un-tick: reopening the item through its step is an un-tick like any other, so the point comes back
        // (the hook fires on both transitions, not only on completion).
        (await ToggleStepAsync(itemId, stepId)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterUntick = await LoadItemAsync(itemId);
        afterUntick.IsDone.Should().BeFalse();
        afterUntick.Streak.Should().Be(0, "un-ticking the step that completed the item gives the point back");
        afterUntick.BestStreak.Should().Be(0);

        // Re-tick: the repeat case, shared with the bulk toggle through UpdateItemStreak.
        (await ToggleStepAsync(itemId, stepId)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterRetick = await LoadItemAsync(itemId);
        afterRetick.IsDone.Should().BeTrue();
        afterRetick.Streak.Should().Be(1, "net zero across an un-tick/re-tick, not a climb");
        afterRetick.BestStreak.Should().Be(1);
    }

    /// <summary>
    /// <c>BeforeToggle</c> (<c>:28-32</c>) calls the <b>single-item</b> <c>TryReset</c>, which deliberately does
    /// not advance <c>LastResetAt</c>, write a completion row, or evaluate the period streak — only the list
    /// overload closes a cycle. (The item's own streak it does settle: done at the boundary is a hit, so the
    /// item here keeps its 4.) This is the endpoint-level guard <c>docs/testing.md</c>'s known-gaps list asks
    /// for (it currently names the unit tests as the whole guard), and it is what stops a "unify the two
    /// overloads" refactor from silently consuming a reset cycle without scoring it.
    /// </summary>
    [Fact]
    public async Task StepToggle_OnAnElapsedPeriod_ResetsTheItemButNotThePeriod()
    {
        long periodId, itemId;
        Guid firstStepId;
        var lastResetAt = DateTime.UtcNow.AddDays(-8);
        await using (var db = CreateDbContext())
        {
            var period = await SeedPeriodAsync(db, lastResetAt: lastResetAt, streak: 3);
            periodId = period.Id;
            // Fully done and stamped with an old cycle: the reset has something to visibly undo.
            var item = await SeedItemAsync(
                db, period, "step-elapsed", isDone: true, streak: 4, lastResetDate: Today.AddDays(-8), stepCount: 2);
            itemId = item.Id;
            firstStepId = item.Steps.OrderBy(s => s.Order).First().Id;
        }

        (await ToggleStepAsync(itemId, firstStepId)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var storedItem = await LoadItemAsync(itemId);
        storedItem.LastResetDate.Should().Be(Today, "the single-item reset stamps the item as fresh");
        storedItem.IsDone.Should().BeFalse("after the reset only one of two steps is ticked");
        storedItem.Steps.OrderBy(s => s.Order).Select(s => s.IsDone)
            .Should().Equal(new[] { true, false }, "the reset wiped both steps, then this request ticked the first");
        storedItem.Streak.Should().Be(4, "the item was done when the cycle closed, so its own streak survives the reset");

        var storedPeriod = await LoadPeriodAsync(periodId);
        storedPeriod.LastResetAt.Should().BeCloseTo(lastResetAt, TimeSpan.FromMilliseconds(1),
            "advancing LastResetAt here would consume the cycle with no streak evaluation and no history row");
        storedPeriod.Streak.Should().Be(3);

        (await LoadCompletionsAsync(periodId)).Should().BeEmpty(
            "only the list overload writes history; the step toggle must leave the cycle open for it");
    }

    // ---- auth: the toggles rely on the global query filter (CQ-12) --------------------------------------

    /// <summary>
    /// Neither subclass filters by <c>UserId</c> — both lean on <c>AppDbContext</c>'s global
    /// <c>IEntityWithUser</c> filter, which is sanctioned by invariant 3 but asserted nowhere for these two
    /// routes (the CRUD auth matrix covers create/update/delete/get-by-id, not the toggles). Verified with
    /// <c>IgnoreQueryFilters</c>, because the filter is exactly the thing under test.
    /// </summary>
    [Fact]
    public async Task Toggle_OnAnotherUsersItem_IsNotFoundAndLeavesTheRowUnchanged()
    {
        long foreignItemId;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            var period = await SeedPeriodAsync(db, lastResetAt: DateTime.UtcNow, userId: OtherUserId);
            foreignItemId = (await SeedItemAsync(db, period, "foreign-bulk", userId: OtherUserId, streak: 2)).Id;
        }

        var response = await ToggleAsync(foreignItemId, forceValue: true);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var verify = CreateDbContext();
        var stored = await verify.Set<RoutineTodoList>().AsNoTracking().IgnoreQueryFilters()
            .FirstAsync(i => i.Id == foreignItemId, CancellationToken);
        stored.IsDone.Should().BeFalse();
        stored.Streak.Should().Be(2);
    }

    [Fact]
    public async Task StepToggle_OnAnotherUsersItem_IsNotFoundAndLeavesTheRowUnchanged()
    {
        long foreignItemId;
        Guid stepId;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            var period = await SeedPeriodAsync(db, lastResetAt: DateTime.UtcNow, userId: OtherUserId);
            var item = await SeedItemAsync(db, period, "foreign-step", userId: OtherUserId, streak: 2, stepCount: 1);
            foreignItemId = item.Id;
            stepId = item.Steps.Single().Id;
        }

        var response = await ToggleStepAsync(foreignItemId, stepId);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var verify = CreateDbContext();
        var stored = await verify.Set<RoutineTodoList>().AsNoTracking().IgnoreQueryFilters()
            .Include(i => i.Steps)
            .FirstAsync(i => i.Id == foreignItemId, CancellationToken);
        stored.IsDone.Should().BeFalse();
        stored.Streak.Should().Be(2);
        stored.Steps.Should().ContainSingle().Which.IsDone.Should().BeFalse();
    }
}
