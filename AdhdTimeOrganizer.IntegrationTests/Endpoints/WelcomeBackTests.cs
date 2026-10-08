using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Testing;
using Sydowwe.Notifications.domain.entity;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// FO5 — coming back after a gap: the stamp that notices the absence (<c>LastActiveStampMiddleware</c>), the card
/// (<c>GET /welcome-back</c>), its answer (<c>POST /welcome-back/fresh-start</c>), the undo and the dismiss.
/// <para>
/// All of it fails <b>silently</b>: a missed stamp is a card that never appears, a wrong pile is a well-formed 200,
/// and a half-applied fresh start looks like edits the user made. So every case asserts on stored rows, and the
/// card is always opened the real way — by a request arriving after <c>user.last_active_at</c> was backdated —
/// never by writing its columns directly, except where the case is about a card that is already open.
/// </para>
/// <para>
/// The host's clock is a replaced <see cref="TimeProvider"/> on a fixed far-future day (2030-03-12, 10:00 UTC), and
/// the fixture user is on UTC. The user was last active on 03-04 at 18:00, so the absence is eight days.
/// </para>
/// </summary>
[Collection("Postgres")]
public class WelcomeBackTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private static readonly DateOnly Today = new(2030, 3, 12);
    private static readonly DateTime Now = new(2030, 3, 12, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LeftAt = new(2030, 3, 4, 18, 0, 0, DateTimeKind.Utc);
    private static DateOnly D(int day) => new(2030, 3, day);

    private long _priorityId;
    private long _listId;

    protected override async Task SeedAsync(DbContext db)
    {
        _priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 491, UserId, "welcome-back", CancellationToken);
        _listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, UserId, "Welcome-back list", ct: CancellationToken);
    }

    // ---------------------------------------------------------------- the stamp

    [Fact(DisplayName = "FO5: the first request after four days away stamps the user and opens the card, whatever the endpoint")]
    public async Task FirstRequestAfterAGap_OpensTheCard()
    {
        await SetLastActiveAsync(LeftAt);
        await using var host = Host();

        // Not the card's own route: the client's first requests go out together, and any of them may be first.
        (await host.CreateClient().GetAsync("api/notification/unread-count", CancellationToken)).EnsureSuccessStatusCode();

        var user = await LoadUserAsync();
        user.LastActiveAt.Should().Be(Now);
        user.WelcomeBackAwayFrom.Should().Be(LeftAt, "the stamp it replaced is where the absence began");
        user.WelcomeBackReturnedAt.Should().Be(Now);

        var card = await GetCardAsync(host.CreateClient());
        card.Should().NotBeNull();
        card!.DaysAway.Should().Be(8);
        card.AwayFrom.Should().Be(LeftAt);
        card.WasMarkedAway.Should().BeFalse();
    }

    [Fact(DisplayName = "FO5: three days away is quiet but earns no card — a long weekend is not a homecoming")]
    public async Task ThreeDaysAway_EarnsNoCard()
    {
        await SetLastActiveAsync(Now.AddDays(-3));
        await using var host = Host();

        (await GetCardAsync(host.CreateClient())).Should().BeNull();

        var user = await LoadUserAsync();
        user.LastActiveAt.Should().Be(Now);
        user.WelcomeBackAwayFrom.Should().BeNull();
        UserPresence.IsAbsent(Now.AddDays(-3), Now).Should().BeTrue("three days is the back-off's threshold");
    }

    [Fact(DisplayName = "FO5: the stamp is hourly — a stamp under an hour old is left alone")]
    public async Task RecentStamp_IsNotRewritten()
    {
        var recent = Now.AddMinutes(-30);
        await SetLastActiveAsync(recent);
        await using var host = Host();

        (await GetCardAsync(host.CreateClient())).Should().BeNull();

        (await LoadUserAsync()).LastActiveAt.Should().Be(recent);
    }

    [Fact(DisplayName = "FO5: a user never measured is stamped and not greeted — the column only accumulates from deploy")]
    public async Task NeverMeasured_IsStampedWithoutACard()
    {
        await SetLastActiveAsync(null);
        await using var host = Host();

        (await GetCardAsync(host.CreateClient())).Should().BeNull();

        (await LoadUserAsync()).LastActiveAt.Should().Be(Now);
    }

    [Fact(DisplayName = "FO5: a card nobody answered lapses two days after the return")]
    public async Task UnansweredCard_Lapses()
    {
        await using (var db = CreateDbContext())
            await db.Set<User>().IgnoreQueryFilters().Where(u => u.Id == UserId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.LastActiveAt, Now.AddMinutes(-5))
                    .SetProperty(u => u.WelcomeBackAwayFrom, LeftAt.AddDays(-3))
                    .SetProperty(u => u.WelcomeBackReturnedAt, Now.AddDays(-2)), CancellationToken);
        await using var host = Host();

        (await GetCardAsync(host.CreateClient())).Should().BeNull();
    }

    // ---------------------------------------------------------------- the card

    [Fact(DisplayName = "FO5: the card names the overdue to-dos, the blocks left open since the absence began, the broken routines and the unread count")]
    public async Task Card_CarriesThePile()
    {
        var pile = await SeedPileAsync();
        await SetLastActiveAsync(LeftAt);
        await using var host = Host();

        var (status, body) = await SendAsync(host.CreateClient().GetAsync("api/welcome-back", CancellationToken));
        status.Should().Be(HttpStatusCode.OK, body);
        var card = Read<WelcomeBackResponse>(body);

        card.OverdueTodos.Count.Should().Be(4, "overdue before the absence still counts; due today, and done, do not");
        card.OverdueTodos.Preview.Select(i => i.Id).Should().Equal(pile.TodoMarch1, pile.TodoMarch8, pile.TodoMarch10);
        card.OverdueTodos.Preview[0].Name.Should().Be("todo-march-1");
        card.OverdueTodos.Preview[0].Date.Should().Be(D(1));

        card.PastTasks.Count.Should().Be(2,
            "open blocks from the day the user left up to yesterday; not before it, not today, not done, skipped or background");
        card.PastTasks.Preview.Select(i => i.Id).Should().Equal(pile.TaskOnLeavingDay, pile.TaskInProgress);
        card.PastTasks.Preview[1].Date.Should().Be(D(6));

        card.BrokenRoutines.Select(r => r.Id).Should().Equal(pile.BrokenRoutine);
        card.UnreadNotifications.Should().Be(3);

        // The wire contract the client codes against.
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        root.GetProperty("daysAway").GetInt32().Should().Be(8);
        root.GetProperty("wasMarkedAway").GetBoolean().Should().BeFalse();
        root.GetProperty("overdueTodos").GetProperty("preview")[0].GetProperty("name").GetString().Should().Be("todo-march-1");
        root.GetProperty("pastTasks").GetProperty("preview")[0].GetProperty("date").GetString().Should().Be("2030-03-04");
        root.GetProperty("brokenRoutines")[0].GetProperty("text").GetString().Should().Be("Daily-broken");
        root.GetProperty("unreadNotifications").GetInt32().Should().Be(3);
    }

    [Fact(DisplayName = "FO5: an absence mostly marked as vacation is announced on the card — FO1's at-least-half rule")]
    public async Task MarkedVacation_IsAnnounced()
    {
        // The missed days are 03-05 .. 03-11, seven of them; four marked is at least half.
        await using (var db = CreateDbContext())
        {
            foreach (var day in new[] { 5, 6, 7, 8 })
                await PlanningTestSeedHelper.SeedCalendarAsync(db, D(day), UserId, CancellationToken, DayType.Vacation);
            await PlanningTestSeedHelper.SeedCalendarAsync(db, D(9), UserId, CancellationToken, DayType.SickDay);
        }
        await SetLastActiveAsync(LeftAt);
        await using var host = Host();

        (await GetCardAsync(host.CreateClient()))!.WasMarkedAway.Should().BeTrue();
    }

    [Fact(DisplayName = "FO5: three marked days out of seven do not make it a vacation")]
    public async Task ThreeMarkedDaysOfSeven_IsNotAnnounced()
    {
        await using (var db = CreateDbContext())
            foreach (var day in new[] { 5, 6, 7 })
                await PlanningTestSeedHelper.SeedCalendarAsync(db, D(day), UserId, CancellationToken, DayType.Vacation);
        await SetLastActiveAsync(LeftAt);
        await using var host = Host();

        (await GetCardAsync(host.CreateClient()))!.WasMarkedAway.Should().BeFalse();
    }

    // ---------------------------------------------------------------- the fresh start

    [Fact(DisplayName = "FO5: a fresh start spreads the to-dos oldest first, closes the blocks as away, reads the pile — in one go — and closes the card")]
    public async Task FreshStart_SpreadCloseAndRead()
    {
        var pile = await SeedPileAsync();
        await SetLastActiveAsync(LeftAt);
        await using var host = Host();
        var client = host.CreateClient();

        var (status, body) = await SendAsync(FreshStartAsync(client, "Spread", 2, "CloseAsAway", markRead: true));
        status.Should().Be(HttpStatusCode.OK, body);
        var result = Read<ApplyFreshStartResponse>(body);

        result.UndoToken.Should().NotBeNullOrEmpty();
        result.RescheduledTodoIds.Should().BeEquivalentTo([pile.TodoMarch1, pile.TodoMarch8, pile.TodoMarch10, pile.TodoMarch11]);
        result.ClosedTaskIds.Should().BeEquivalentTo([pile.TaskOnLeavingDay, pile.TaskInProgress]);
        result.CarriedTaskIds.Should().BeEmpty();
        result.NotificationsMarkedRead.Should().Be(3);

        // Round-robin, oldest first: today, tomorrow, today, tomorrow.
        (await LoadTodoAsync(pile.TodoMarch1)).DueDate.Should().Be(Today);
        (await LoadTodoAsync(pile.TodoMarch8)).DueDate.Should().Be(Today.AddDays(1));
        (await LoadTodoAsync(pile.TodoMarch10)).DueDate.Should().Be(Today);
        var march11 = await LoadTodoAsync(pile.TodoMarch11);
        march11.DueDate.Should().Be(Today.AddDays(1));
        march11.PostponeCount.Should().Be(1, "a spread is a postponement under FO3's rule, as the overdue banner's move is");
        march11.FirstDueDate.Should().Be(D(11));
        (await LoadTodoAsync(pile.TodoDueToday)).DueDate.Should().Be(Today, "not overdue, not in the pile");

        foreach (var id in new[] { pile.TaskOnLeavingDay, pile.TaskInProgress })
        {
            var task = await LoadTaskAsync(id);
            task.Status.Should().Be(PlannerTaskStatus.Cancelled);
            task.SkipReasonKey.Should().Be(SkipReasonKey.Away);
            task.ActualStartTime.Should().BeNull();
        }
        (await LoadTaskAsync(pile.TaskBeforeLeaving)).Status.Should().Be(PlannerTaskStatus.NotStarted,
            "a block left open before the absence is not the card's");

        await using (var db = CreateDbContext())
            (await db.Set<Notification>().CountAsync(n => n.UserId == UserId && !n.IsRead, CancellationToken)).Should().Be(0);

        (await GetCardAsync(client)).Should().BeNull("answering the card closes it");

        var (again, againBody) = await SendAsync(FreshStartAsync(client, "Someday", 0, "Keep", markRead: false));
        again.Should().Be(HttpStatusCode.Conflict, againBody);
    }

    [Fact(DisplayName = "FO5: carrying moves the blocks to today with FO3's carry record, but a repeating rule's occurrence is closed instead")]
    public async Task FreshStart_CarryToToday()
    {
        long carried, occurrence;
        await using (var db = CreateDbContext())
        {
            var activity = await PlanningTestSeedHelper.SeedActivityAsync(db, "carry-me", UserId, CancellationToken);
            var day = await PlanningTestSeedHelper.SeedCalendarAsync(db, D(6), UserId, CancellationToken);
            carried = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activity, day, new TimeOnly(9, 0), new TimeOnly(10, 0), UserId, ct: CancellationToken);

            var ruleActivity = await PlanningTestSeedHelper.SeedActivityAsync(db, "gym", UserId, CancellationToken);
            var rule = await PlanningTestSeedHelper.SeedRepeatingPlannerTaskAsync(db, ruleActivity, UserId, ct: CancellationToken);
            occurrence = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, ruleActivity, day, new TimeOnly(18, 0), new TimeOnly(19, 0), UserId, ct: CancellationToken);
            await db.Set<PlannerTask>().Where(t => t.Id == occurrence)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.SourceRepeatingTaskId, rule), CancellationToken);
        }
        await SetLastActiveAsync(LeftAt);
        await using var host = Host();

        var (status, body) = await SendAsync(FreshStartAsync(host.CreateClient(), "Keep", 0, "CarryToToday", markRead: false));
        status.Should().Be(HttpStatusCode.OK, body);
        var result = Read<ApplyFreshStartResponse>(body);
        result.CarriedTaskIds.Should().Equal(carried);
        result.ClosedTaskIds.Should().Equal(occurrence);

        var task = await LoadTaskAsync(carried);
        task.Calendar.Date.Should().Be(Today);
        task.StartTime.Should().Be(new TimeOnly(9, 0), "a carried block keeps its time of day");
        task.Status.Should().Be(PlannerTaskStatus.NotStarted);
        task.OriginalDate.Should().Be(D(6));
        task.OriginalStartTime.Should().Be(new TimeOnly(9, 0));
        task.CarryOverCount.Should().Be(1);

        var rulesOwn = await LoadTaskAsync(occurrence);
        rulesOwn.Status.Should().Be(PlannerTaskStatus.Cancelled, "the rule places today's own occurrence");
        rulesOwn.SkipReasonKey.Should().Be(SkipReasonKey.Away);
        rulesOwn.Calendar.Date.Should().Be(D(6));
    }

    [Fact(DisplayName = "FO5: someday clears the due date and time, and is not a postponement")]
    public async Task FreshStart_Someday()
    {
        var item = await SeedTodoAsync("someday", D(2));
        await using (var db = CreateDbContext())
            await db.Set<TodoListItem>().Where(i => i.Id == item)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.DueTime, new TimeOnly(14, 0)), CancellationToken);
        await SetLastActiveAsync(LeftAt);
        await using var host = Host();

        var (status, body) = await SendAsync(FreshStartAsync(host.CreateClient(), "Someday", 0, "Keep", markRead: false));
        status.Should().Be(HttpStatusCode.OK, body);

        var stored = await LoadTodoAsync(item);
        stored.DueDate.Should().BeNull();
        stored.DueTime.Should().BeNull();
        stored.PostponeCount.Should().Be(0);
        stored.FirstDueDate.Should().BeNull();
    }

    [Fact(DisplayName = "FO5: a spread over zero days is a 400, and nothing is written")]
    public async Task FreshStart_SpreadNeedsDays()
    {
        var item = await SeedTodoAsync("invalid", D(2));
        await SetLastActiveAsync(LeftAt);
        await using var host = Host();

        var (status, _) = await SendAsync(FreshStartAsync(host.CreateClient(), "Spread", 0, "Keep", markRead: false));
        status.Should().Be(HttpStatusCode.BadRequest);
        (await LoadTodoAsync(item)).DueDate.Should().Be(D(2));
    }

    // ---------------------------------------------------------------- undo and dismiss

    [Fact(DisplayName = "FO5: undo puts back every row the user has not touched since, reopens the card, and is spent once")]
    public async Task Undo_RestoresUntouchedRows_AndReopensTheCard()
    {
        var pile = await SeedPileAsync();
        await SetLastActiveAsync(LeftAt);
        await using var host = Host();
        var client = host.CreateClient();

        var applied = Read<ApplyFreshStartResponse>((await SendAsync(FreshStartAsync(client, "Spread", 3, "CloseAsAway", markRead: true))).Body);

        // The user acts on two rows before thinking better of the fresh start.
        await using (var db = CreateDbContext())
        {
            await db.Set<TodoListItem>().Where(i => i.Id == pile.TodoMarch8)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.IsDone, true), CancellationToken);
            await db.Set<PlannerTask>().Where(t => t.Id == pile.TaskInProgress)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, PlannerTaskStatus.Completed), CancellationToken);
        }

        var (status, body) = await SendAsync(client.PostAsJsonAsync("api/welcome-back/fresh-start/undo",
            new { applied.UndoToken }, JsonOpts, CancellationToken));
        status.Should().Be(HttpStatusCode.OK, body);
        var undone = Read<UndoFreshStartResponse>(body);

        undone.RestoredTodoIds.Should().BeEquivalentTo([pile.TodoMarch1, pile.TodoMarch10, pile.TodoMarch11]);
        undone.RestoredTaskIds.Should().Equal(pile.TaskOnLeavingDay);
        undone.NotificationsRestored.Should().Be(3);

        var march1 = await LoadTodoAsync(pile.TodoMarch1);
        march1.DueDate.Should().Be(D(1));
        march1.PostponeCount.Should().Be(0, "the postponement goes with the move it recorded");
        march1.FirstDueDate.Should().BeNull();
        (await LoadTodoAsync(pile.TodoMarch8)).IsDone.Should().BeTrue("a row changed since is left as the user left it");

        var leavingDay = await LoadTaskAsync(pile.TaskOnLeavingDay);
        leavingDay.Status.Should().Be(PlannerTaskStatus.NotStarted);
        leavingDay.SkipReasonKey.Should().BeNull();
        (await LoadTaskAsync(pile.TaskInProgress)).Status.Should().Be(PlannerTaskStatus.Completed);

        await using (var db = CreateDbContext())
            (await db.Set<Notification>().CountAsync(n => n.UserId == UserId && !n.IsRead, CancellationToken)).Should().Be(3);

        (await GetCardAsync(client)).Should().NotBeNull("undo reopens the card, so the user can choose again");

        var (second, _) = await SendAsync(client.PostAsJsonAsync("api/welcome-back/fresh-start/undo",
            new { applied.UndoToken }, JsonOpts, CancellationToken));
        second.Should().Be(HttpStatusCode.NotFound, "an undo token is spent once");
    }

    [Fact(DisplayName = "FO5: an unknown undo token is a 404")]
    public async Task Undo_UnknownToken_IsNotFound()
    {
        await using var host = Host();
        var (status, _) = await SendAsync(host.CreateClient().PostAsJsonAsync("api/welcome-back/fresh-start/undo",
            new { UndoToken = Guid.NewGuid().ToString("N") }, JsonOpts, CancellationToken));
        status.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = "FO5: dismiss closes the card and leaves everything on it as it was")]
    public async Task Dismiss_ClosesTheCard_AndTouchesNothing()
    {
        var pile = await SeedPileAsync();
        await SetLastActiveAsync(LeftAt);
        await using var host = Host();
        var client = host.CreateClient();
        (await GetCardAsync(client)).Should().NotBeNull();

        var (status, _) = await SendAsync(client.PostAsync("api/welcome-back/dismiss", null, CancellationToken));
        status.Should().Be(HttpStatusCode.NoContent);

        (await GetCardAsync(client)).Should().BeNull();
        (await LoadTodoAsync(pile.TodoMarch1)).DueDate.Should().Be(D(1));
        (await LoadTaskAsync(pile.TaskOnLeavingDay)).Status.Should().Be(PlannerTaskStatus.NotStarted);
    }

    // ---------------------------------------------------------------- seeding

    private sealed record Pile(
        long TodoMarch1, long TodoMarch8, long TodoMarch10, long TodoMarch11, long TodoDueToday,
        long TaskBeforeLeaving, long TaskOnLeavingDay, long TaskInProgress,
        long BrokenRoutine);

    /// <summary>
    /// The eight-day pile: four overdue to-dos (one from before the absence), a due-today and a done one that are not
    /// overdue; open blocks before, on and during the absence plus four that are not open; four routines of which one
    /// broke during the absence; three unread notifications and one read.
    /// </summary>
    private async Task<Pile> SeedPileAsync()
    {
        var todoMarch1 = await SeedTodoAsync("todo-march-1", D(1));
        var todoMarch8 = await SeedTodoAsync("todo-march-8", D(8));
        var todoMarch10 = await SeedTodoAsync("todo-march-10", D(10));
        var todoMarch11 = await SeedTodoAsync("todo-march-11", D(11));
        var todoDueToday = await SeedTodoAsync("todo-due-today", Today);
        await SeedTodoAsync("todo-done", D(5), isDone: true);

        await using var db = CreateDbContext();

        var work = await PlanningTestSeedHelper.SeedActivityAsync(db, "work", UserId, CancellationToken);
        var march3 = await PlanningTestSeedHelper.SeedCalendarAsync(db, D(3), UserId, CancellationToken);
        var march4 = await PlanningTestSeedHelper.SeedCalendarAsync(db, D(4), UserId, CancellationToken);
        var march6 = await PlanningTestSeedHelper.SeedCalendarAsync(db, D(6), UserId, CancellationToken);
        var today = await PlanningTestSeedHelper.SeedCalendarAsync(db, Today, UserId, CancellationToken);

        var beforeLeaving = await TaskAsync(db, work, march3, 9);
        var onLeavingDay = await TaskAsync(db, work, march4, 20);
        var inProgress = await TaskAsync(db, work, march6, 9, PlannerTaskStatus.InProgress);
        await TaskAsync(db, work, march6, 11, PlannerTaskStatus.Completed);
        await TaskAsync(db, work, march6, 13, PlannerTaskStatus.Cancelled);
        var background = await TaskAsync(db, work, march6, 15);
        await db.Set<PlannerTask>().Where(t => t.Id == background)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsBackground, true), CancellationToken);
        await TaskAsync(db, work, today, 9);

        var broken = await RoutineAsync(db, "Daily-broken", length: 1, streak: 0, periodEnd: D(9), completed: 0);
        var frozen = await RoutineAsync(db, "Daily-frozen", length: 2, streak: 0, periodEnd: D(9), completed: 0);
        await db.Set<RoutinePeriodCompletion>().Where(c => c.TimePeriodId == frozen)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.IsFrozen, true)
                .SetProperty(c => c.FrozenAt, Now), CancellationToken);
        await RoutineAsync(db, "Weekly-kept", length: 7, streak: 3, periodEnd: D(9), completed: 0);
        await RoutineAsync(db, "Broken-before", length: 3, streak: 0, periodEnd: D(2), completed: 0);

        for (var i = 0; i < 3; i++)
            db.Add(new Notification { UserId = UserId, Type = NotificationType.Test, PayloadJson = "{}" });
        db.Add(new Notification { UserId = UserId, Type = NotificationType.Test, PayloadJson = "{}", IsRead = true, ReadAt = Now });
        await db.SaveChangesAsync(CancellationToken);

        return new Pile(todoMarch1, todoMarch8, todoMarch10, todoMarch11, todoDueToday,
            beforeLeaving, onLeavingDay, inProgress, broken);
    }

    /// <summary>One activity per item: <c>todo_list_item</c> is unique on <c>(UserId, ActivityId, TodoListId)</c>.</summary>
    private async Task<long> SeedTodoAsync(string name, DateOnly dueDate, bool isDone = false)
    {
        await using var db = CreateDbContext();
        var activity = await PlanningTestSeedHelper.SeedActivityAsync(db, name, UserId, CancellationToken);
        return await TodoListTestSeedHelper.SeedTodoListItemAsync(
            db, activity, _priorityId, _listId, UserId, isDone: isDone, ct: CancellationToken, dueDate: dueDate);
    }

    private async Task<long> TaskAsync(DbContext db, long activity, long calendar, int hour,
        PlannerTaskStatus status = PlannerTaskStatus.NotStarted) =>
        await PlanningTestSeedHelper.SeedPlannerTaskAsync(
            db, activity, calendar, new TimeOnly(hour, 0), new TimeOnly(hour, 30), UserId, status, ct: CancellationToken);

    /// <summary>A routine period with one closed cycle. One period per length per user (a unique index), so each
    /// caller picks its own.</summary>
    private async Task<long> RoutineAsync(DbContext db, string text, int length, int streak, DateOnly periodEnd, int completed)
    {
        var period = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(
            db, UserId, text, lengthInDays: length, streakThreshold: 100, ct: CancellationToken);
        await db.Set<RoutineTimePeriod>().Where(p => p.Id == period)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Streak, streak), CancellationToken);
        await TodoListTestSeedHelper.SeedRoutinePeriodCompletionAsync(
            db, period, periodEnd.AddDays(-1), periodEnd, completedCount: completed, totalCount: 2, ct: CancellationToken);
        return period;
    }

    private async Task SetLastActiveAsync(DateTime? lastActiveAt)
    {
        await using var db = CreateDbContext();
        await db.Set<User>().IgnoreQueryFilters().Where(u => u.Id == UserId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.LastActiveAt, lastActiveAt)
                .SetProperty(u => u.WelcomeBackAwayFrom, (DateTime?)null)
                .SetProperty(u => u.WelcomeBackReturnedAt, (DateTime?)null), CancellationToken);
    }

    // ---------------------------------------------------------------- loading

    private async Task<User> LoadUserAsync()
    {
        await using var db = CreateDbContext();
        return await db.Set<User>().AsNoTracking().IgnoreQueryFilters().SingleAsync(u => u.Id == UserId, CancellationToken);
    }

    private async Task<TodoListItem> LoadTodoAsync(long id)
    {
        await using var db = CreateDbContext();
        return await db.Set<TodoListItem>().AsNoTracking().IgnoreQueryFilters().SingleAsync(i => i.Id == id, CancellationToken);
    }

    private async Task<PlannerTask> LoadTaskAsync(long id)
    {
        await using var db = CreateDbContext();
        return await db.Set<PlannerTask>().AsNoTracking().IgnoreQueryFilters().Include(t => t.Calendar)
            .SingleAsync(t => t.Id == id, CancellationToken);
    }

    // ---------------------------------------------------------------- host and HTTP

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private ITestClientFactory Host() =>
        CreateFactory(TestRoles.AdminAndUser, configureServices: services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new Clock(new DateTimeOffset(Now)));
        });

    private static async Task<WelcomeBackResponse?> GetCardAsync(HttpClient client)
    {
        var (status, body) = await SendAsync(client.GetAsync("api/welcome-back", CancellationToken));
        if (status == HttpStatusCode.NoContent)
            return null;

        status.Should().Be(HttpStatusCode.OK, body);
        return Read<WelcomeBackResponse>(body);
    }

    private static Task<HttpResponseMessage> FreshStartAsync(
        HttpClient client, string todos, int spreadOverDays, string pastTasks, bool markRead) =>
        client.PostAsJsonAsync("api/welcome-back/fresh-start", new
        {
            Todos = todos,
            SpreadOverDays = spreadOverDays,
            PastTasks = pastTasks,
            MarkNotificationsRead = markRead
        }, JsonOpts, CancellationToken);

    private static async Task<(HttpStatusCode Status, string Body)> SendAsync(Task<HttpResponseMessage> request)
    {
        using var response = await request;
        return (response.StatusCode, await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private static T Read<T>(string body) => JsonSerializer.Deserialize<T>(body, JsonOpts)!;
}
