using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Nodes;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.application.notification;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.TodoLists.application.notification;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.kind;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.Testing;
using Sydowwe.Notifications.application.job;
using Sydowwe.Notifications.domain.entity;
using Sydowwe.Notifications.infrastructure;
using Sydowwe.Notifications.infrastructure.email;
using Sydowwe.Notifications.infrastructure.webPush;
using Sydowwe.Reminders.application.job;
using Sydowwe.Reminders.domain.entity;
using Sydowwe.Reminders.domain.@enum;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// P2-03 — acting on a notification from the lock screen. Two halves, both asserted on what a real device would get
/// and on the rows each tap leaves behind, never on a route alone:
/// <list type="bullet">
/// <item><b>The push document</b> carries each kind's declared buttons (<see cref="NotificationCatalogTests.Actions"/>)
/// in the recipient's own language, on the live path and on the quiet-hours flush, and a Snooze only where there is a
/// reminder occurrence to defer.</item>
/// <item><b><c>POST /notification/{id}/action/{action}</c></b> does what the button says, on the caller's own row only,
/// for buttons that notification actually offered, idempotently, and marks the row read — or answers 409 and leaves it
/// unread when the thing is gone, so the worker opens the app on it instead.</item>
/// </list>
/// Every failure here is silent in production: a missing button is a push that merely opens, and a button that does the
/// wrong thing does it on a lock screen nobody is looking at afterwards.
/// <para>
/// <b>Planner cases pin the clock</b> to Tuesday 29 September 2020, for the reason <c>PlannerSlipNudgeJobTests</c>
/// gives — and because "now on the user's clock" is the whole point of those buttons. The to-do case runs on the real
/// clock with the due date days ahead, as <c>TodoListItemDueReminderTests</c> does, so the real scan delivers and
/// re-delivers it.
/// </para>
/// </summary>
[Collection("Postgres")]
public class NotificationPushActionTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private const long EnglishUserId = 90_301;
    private const long SlovakUserId = 90_302;
    private const long OtherUserId = 90_303;

    private static readonly DateOnly Tuesday = new(2020, 9, 29);

    private static readonly KeyValuePair<string, string?>[] PushOnEmailOff =
    [
        new($"{EmailNotificationOptions.SectionName}:Enabled", "false"),
        new($"{PushNotificationOptions.SectionName}:VapidSubject", "mailto:test@example.com"),
        new($"{PushNotificationOptions.SectionName}:VapidPublicKey", "test-public-key"),
        new($"{PushNotificationOptions.SectionName}:VapidPrivateKey", "test-private-key")
    ];

    protected override async Task SeedAsync(DbContext db)
    {
        db.Add(NewUser(EnglishUserId, AvailableLocales.En));
        db.Add(NewUser(SlovakUserId, AvailableLocales.Sk));
        db.Add(NewUser(OtherUserId, AvailableLocales.En));
        foreach (var userId in new[] { UserId, EnglishUserId, SlovakUserId })
            db.Add(new PushSubscription { UserId = userId, Endpoint = EndpointOf(userId), P256dh = "test-p256dh", Auth = "test-auth" });
        await db.SaveChangesAsync();
    }

    // ---- the push document ------------------------------------------------------------------------

    [Fact(DisplayName = "P2-03: a reminder's push carries Snooze and Dismiss, worded in each recipient's language, and data.id")]
    public async Task Push_CarriesTheDeclaredButtons_InEachRecipientsLanguage()
    {
        var push = new CapturingWebPushSender();
        await using var factory = CreatePushFactory(push);

        await NotifyAsync(factory, NotificationRecipients.Users([EnglishUserId, SlovakUserId]),
            PlanningNotificationTypes.PersonalReminder, new PersonalReminderPayload(ReminderId: 1, Title: "Call the bank"),
            new NotificationReminderOccurrence(ReminderDefinitionId: 77, OccurrenceAt: DateTimeOffset.UtcNow));

        Actions(push.DocumentFor(EnglishUserId)).Should().Equal(("snooze", "Snooze 10 min"), ("dismiss", "Dismiss"));
        Actions(push.DocumentFor(SlovakUserId)).Should().Equal(("snooze", "Odložiť o 10 min"), ("dismiss", "Zavrieť"));

        await using var db = CreateDbContext();
        var row = await db.Set<Notification>().AsNoTracking().SingleAsync(n => n.UserId == EnglishUserId, CancellationToken);
        push.DocumentFor(EnglishUserId)["data"]!["id"]!.GetValue<long>().Should().Be(row.Id, "the worker posts the action back against data.id");
        row.ReminderDefinitionId.Should().Be(77, "the occurrence is stored on the row, which is what a later Snooze reads");
    }

    [Fact(DisplayName = "P2-03: with no reminder occurrence to defer, the push offers no Snooze")]
    public async Task Push_WithoutAReminderOccurrence_OffersNoSnooze()
    {
        var push = new CapturingWebPushSender();
        await using var factory = CreatePushFactory(push);

        await NotifyAsync(factory, NotificationRecipients.User(EnglishUserId),
            PlanningNotificationTypes.PersonalReminder, new PersonalReminderPayload(ReminderId: 1, Title: "Call the bank"));

        Actions(push.DocumentFor(EnglishUserId)).Should().Equal(("dismiss", "Dismiss"));
    }

    [Fact(DisplayName = "P2-03: a kind that declares no buttons pushes no actions key at all")]
    public async Task Push_KindWithoutButtons_HasNoActionsKey()
    {
        var push = new CapturingWebPushSender();
        await using var factory = CreatePushFactory(push);

        await NotifyAsync(factory, NotificationRecipients.User(EnglishUserId), FrameworkNotificationTypes.Test, new TestNotificationPayload("plain"));

        push.DocumentFor(EnglishUserId).ContainsKey("actions").Should().BeFalse();
    }

    /// <summary>A reminder held overnight by quiet hours must still be snoozable when the flush finally pushes it.</summary>
    [Fact(DisplayName = "P2-03: the quiet-hours flush pushes the same buttons, Snooze included")]
    public async Task DeferredFlush_CarriesTheButtons()
    {
        var push = new CapturingWebPushSender();
        await using var factory = CreatePushFactory(push);
        var flushAt = DateTimeOffset.UtcNow;

        await using (var db = CreateDbContext())
        {
            db.Add(new Notification
            {
                UserId = EnglishUserId,
                Type = TodoListsNotificationTypes.TodoItemDue,
                PayloadJson = """{"todoListItemId":5,"title":"Pay rent","minutesBefore":30}""",
                DeferredUntil = flushAt.AddMinutes(-1),
                ReminderDefinitionId = 12,
                ReminderOccurrenceAt = flushAt.AddHours(-8)
            });
            await db.SaveChangesAsync(CancellationToken);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetServices<IScheduledJobHandler>()
                .OfType<FlushDeferredNotificationsJobHandler>()
                .Single(h => h.Key == FlushDeferredNotificationsJobHandler.HandlerKey)
                .FlushDeferredAsync(flushAt, CancellationToken);

        Actions(push.DocumentFor(EnglishUserId)).Should().Equal(("done", "Done"), ("snooze", "Snooze 1 h"));
    }

    // ---- the endpoint: what may be acted on -------------------------------------------------------

    [Fact(DisplayName = "P2-03: another user's notification, an undeclared button, an unknown one and a Snooze with nothing to defer are 404s that change nothing")]
    public async Task Action_NotOffered_Is404_AndChangesNothing()
    {
        var client = CreateClient();
        var foreign = await SeedNotificationAsync(OtherUserId, PlanningNotificationTypes.PersonalReminder, """{"reminderId":1}""", reminderDefinitionId: 1);
        var mine = await SeedNotificationAsync(UserId, PlanningNotificationTypes.PersonalReminder, """{"reminderId":1}""");

        (await PostActionAsync(client, foreign, "dismiss")).Should().Be(HttpStatusCode.NotFound, "not mine");
        (await PostActionAsync(client, mine, "start")).Should().Be(HttpStatusCode.NotFound, "a reminder never offered Start");
        (await PostActionAsync(client, mine, "explode")).Should().Be(HttpStatusCode.NotFound);
        (await PostActionAsync(client, mine, "snooze")).Should().Be(HttpStatusCode.NotFound,
            "this row delivered no reminder occurrence, so its push never showed Snooze");

        await using var db = CreateDbContext();
        (await db.Set<Notification>().AsNoTracking().Where(n => n.Id == foreign || n.Id == mine).ToListAsync(CancellationToken))
            .Should().OnlyContain(n => !n.IsRead);
    }

    /// <summary>Dismiss is "seen it", not the bell's delete: one stray tap on a lock screen must not erase history.</summary>
    [Fact(DisplayName = "P2-03: Dismiss marks the notification read and keeps it; a second tap is still a 204")]
    public async Task Dismiss_MarksRead_AndKeepsTheRow()
    {
        var client = CreateClient();
        var id = await SeedNotificationAsync(UserId, PlanningNotificationTypes.PersonalReminder, """{"reminderId":1}""");

        (await PostActionAsync(client, id, "dismiss")).Should().Be(HttpStatusCode.NoContent);
        (await PostActionAsync(client, id, "Dismiss")).Should().Be(HttpStatusCode.NoContent, "idempotent, and the id is case-insensitive");

        await using var db = CreateDbContext();
        (await db.Set<Notification>().AsNoTracking().SingleAsync(n => n.Id == id, CancellationToken)).IsRead.Should().BeTrue();
    }

    // ---- the to-do: Snooze through the real scan, then Done ---------------------------------------

    /// <summary>
    /// The whole loop on the real Reminders scan: the due push carries the occurrence; Snooze writes one marker however
    /// often it is retried; the scan re-delivers it as a new row carrying the same occurrence; Done on that one ticks the
    /// item, withdraws its reminder, and stays done when retried.
    /// </summary>
    [Fact(DisplayName = "P2-03: to-do Snooze re-delivers through the scan, and Done on the re-delivery ticks it off for good")]
    public async Task TodoItemDue_Snooze_ThenDone()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: PushOnEmailOff);
        var client = factory.CreateClient();
        var dueDay = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
        var itemId = await CreateTodoItemAsync(client, "file taxes", dueDay, new TimeOnly(12, 0));
        var occurrence = new DateTimeOffset(dueDay.ToDateTime(new TimeOnly(11, 30)), TimeSpan.Zero);

        await FireScanAsync(factory, occurrence.AddMinutes(1));
        var first = await SingleTodoNotificationAsync();
        first.ReminderOccurrenceAt.Should().Be(occurrence);

        (await PostActionAsync(client, first.Id, "snooze")).Should().Be(HttpStatusCode.NoContent);
        (await PostActionAsync(client, first.Id, "snooze")).Should().Be(HttpStatusCode.NoContent, "a retried request");

        await using (var db = CreateDbContext())
        {
            var marker = await db.Set<ReminderOccurrenceAction>().AsNoTracking()
                .SingleAsync(a => a.ReminderDefinitionId == first.ReminderDefinitionId, CancellationToken);
            marker.ActionType.Should().Be(ReminderActionType.Snooze, "one marker, not one per retry");
            marker.OccurrenceAt.Should().Be(occurrence);
            marker.UserId.Should().Be(UserId);
            marker.SnoozeUntil.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(60), TimeSpan.FromMinutes(2),
                "the minutes come from the host's declaration, not from the client");
            (await db.Set<Notification>().AsNoTracking().SingleAsync(n => n.Id == first.Id, CancellationToken)).IsRead.Should().BeTrue();
        }

        await FireScanAsync(factory, occurrence.AddDays(1));
        Notification redelivered;
        await using (var db = CreateDbContext())
            redelivered = await db.Set<Notification>().AsNoTracking()
                .SingleAsync(n => n.Type == TodoListsNotificationTypes.TodoItemDue && n.Id != first.Id, CancellationToken);
        redelivered.ReminderOccurrenceAt.Should().Be(occurrence, "the re-delivery carries the original occurrence, so it can be snoozed again");

        (await PostActionAsync(client, redelivered.Id, "done")).Should().Be(HttpStatusCode.NoContent);
        (await PostActionAsync(client, redelivered.Id, "done")).Should().Be(HttpStatusCode.NoContent, "a retried Done must not tick it back open");

        await using (var db = CreateDbContext())
        {
            (await db.Set<TodoListItem>().AsNoTracking().SingleAsync(i => i.Id == itemId, CancellationToken)).IsDone.Should().BeTrue();
            (await db.Set<ReminderDefinition>().AsNoTracking().IgnoreQueryFilters()
                    .SingleAsync(d => d.Id == first.ReminderDefinitionId, CancellationToken))
                .Status.Should().Be(ReminderStatus.Cancelled, "ticking an item off withdraws its reminder, as in the app");
        }
    }

    [Fact(DisplayName = "P2-03: Snooze on a reminder its owner has withdrawn is a 409 that writes nothing and leaves the row unread")]
    public async Task Snooze_OnACancelledReminder_IsGone()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: PushOnEmailOff);
        var client = factory.CreateClient();
        var dueDay = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
        var itemId = await CreateTodoItemAsync(client, "withdrawn", dueDay, new TimeOnly(12, 0));
        await FireScanAsync(factory, new DateTimeOffset(dueDay.ToDateTime(new TimeOnly(11, 31)), TimeSpan.Zero));
        var notification = await SingleTodoNotificationAsync();

        // Ticked off in the app after the push arrived: the reminder is cancelled.
        (await client.PatchAsJsonAsync("api/todo-list-item/toggle-is-done", new { ids = new[] { itemId }, forceValue = true }, JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await PostActionAsync(client, notification.Id, "snooze")).Should().Be(HttpStatusCode.Conflict);

        await using var db = CreateDbContext();
        (await db.Set<ReminderOccurrenceAction>().AnyAsync(CancellationToken)).Should().BeFalse();
        (await db.Set<Notification>().AsNoTracking().SingleAsync(n => n.Id == notification.Id, CancellationToken)).IsRead.Should().BeFalse(
            "the app is where the user sees what happened; the bell keeps it for them");
    }

    // ---- the planner, on a pinned clock -----------------------------------------------------------

    [Fact(DisplayName = "P2-03: Start begins the block now on the user's clock; retried it changes nothing, and Skip after it is a 409")]
    public async Task PlannedBlockStarting_Start()
    {
        await using var factory = CreatePinnedFactory(At(13, 50));
        var client = factory.CreateClient();
        var taskId = await SeedBlockAsync(new TimeOnly(14, 0), new TimeOnly(15, 0));
        var id = await SeedNotificationAsync(UserId, PlanningNotificationTypes.PlannedBlockStarting, $$"""{"plannerTaskId":{{taskId}},"minutesBefore":10}""");

        (await PostActionAsync(client, id, "start")).Should().Be(HttpStatusCode.NoContent);
        (await PostActionAsync(client, id, "start")).Should().Be(HttpStatusCode.NoContent);

        var task = await ReadTaskAsync(taskId);
        task.Status.Should().Be(PlannerTaskStatus.InProgress);
        task.ActualStartTime.Should().Be(new TimeOnly(13, 50), "started when the user tapped, not at the planned start");

        (await PostActionAsync(client, id, "skip")).Should().Be(HttpStatusCode.Conflict, "a block already under way cannot be skipped from its reminder");
        (await ReadTaskAsync(taskId)).Status.Should().Be(PlannerTaskStatus.InProgress);
    }

    [Fact(DisplayName = "P2-03: Skip cancels a block that has not started, with no reason")]
    public async Task PlannedBlockStarting_Skip()
    {
        await using var factory = CreatePinnedFactory(At(13, 50));
        var taskId = await SeedBlockAsync(new TimeOnly(14, 0), new TimeOnly(15, 0));
        var id = await SeedNotificationAsync(UserId, PlanningNotificationTypes.PlannedBlockStarting, $$"""{"plannerTaskId":{{taskId}}}""");

        (await PostActionAsync(factory.CreateClient(), id, "skip")).Should().Be(HttpStatusCode.NoContent);

        var task = await ReadTaskAsync(taskId);
        task.Status.Should().Be(PlannerTaskStatus.Cancelled);
        task.SkipReason.Should().BeNull();
        task.SkipReasonKey.Should().BeNull();
    }

    [Fact(DisplayName = "P2-03: Done on an overrunning block completes it now, keeping the planned start as its actual one")]
    public async Task PlannedBlockOverrun_Done()
    {
        await using var factory = CreatePinnedFactory(At(14, 10));
        var taskId = await SeedBlockAsync(new TimeOnly(12, 0), new TimeOnly(14, 0), PlannerTaskStatus.InProgress);
        var id = await SeedNotificationAsync(UserId, PlanningNotificationTypes.PlannedBlockOverrun, $$"""{"plannerTaskId":{{taskId}},"minutesOver":10}""");

        (await PostActionAsync(factory.CreateClient(), id, "done")).Should().Be(HttpStatusCode.NoContent);

        var task = await ReadTaskAsync(taskId);
        task.Status.Should().Be(PlannerTaskStatus.Completed);
        task.ActualStartTime.Should().Be(new TimeOnly(12, 0), "no start was recorded, so the planned one stands in — the day view's rule");
        task.ActualEndTime.Should().Be(new TimeOnly(14, 10));
    }

    /// <summary>
    /// +15 from <i>now</i>, not from the old end — 14:00 + 15 would already be behind a 14:20 tap and ring again at once —
    /// and a retry finds the block with time left and leaves it alone.
    /// </summary>
    [Fact(DisplayName = "P2-03: +15 min ends an overrunning block fifteen minutes from now; a retry does not add another fifteen")]
    public async Task PlannedBlockOverrun_Extend()
    {
        await using var factory = CreatePinnedFactory(At(14, 20));
        var client = factory.CreateClient();
        var taskId = await SeedBlockAsync(new TimeOnly(12, 0), new TimeOnly(14, 0), PlannerTaskStatus.InProgress);
        var id = await SeedNotificationAsync(UserId, PlanningNotificationTypes.PlannedBlockOverrun, $$"""{"plannerTaskId":{{taskId}},"minutesOver":20}""");

        (await PostActionAsync(client, id, "extend")).Should().Be(HttpStatusCode.NoContent);
        (await PostActionAsync(client, id, "extend")).Should().Be(HttpStatusCode.NoContent);

        var task = await ReadTaskAsync(taskId);
        task.EndTime.Should().Be(new TimeOnly(14, 35));
        task.Status.Should().Be(PlannerTaskStatus.InProgress);
    }

    [Fact(DisplayName = "P2-03: +15 min late at night stops at 23:59 — a span may not wrap past midnight")]
    public async Task PlannedBlockOverrun_Extend_StopsAtMidnight()
    {
        await using var factory = CreatePinnedFactory(At(23, 52));
        var taskId = await SeedBlockAsync(new TimeOnly(22, 0), new TimeOnly(23, 45), PlannerTaskStatus.InProgress);
        var id = await SeedNotificationAsync(UserId, PlanningNotificationTypes.PlannedBlockOverrun, $$"""{"plannerTaskId":{{taskId}}}""");

        (await PostActionAsync(factory.CreateClient(), id, "extend")).Should().Be(HttpStatusCode.NoContent);

        (await ReadTaskAsync(taskId)).EndTime.Should().Be(new TimeOnly(23, 59));
    }

    [Fact(DisplayName = "P2-03: a button on a block that has since been deleted is a 409, and the notification stays unread")]
    public async Task PlannerAction_OnADeletedBlock_IsGone()
    {
        await using var factory = CreatePinnedFactory(At(13, 50));
        var id = await SeedNotificationAsync(UserId, PlanningNotificationTypes.PlannedBlockStarting, """{"plannerTaskId":987654}""");

        (await PostActionAsync(factory.CreateClient(), id, "start")).Should().Be(HttpStatusCode.Conflict);

        await using var db = CreateDbContext();
        (await db.Set<Notification>().AsNoTracking().SingleAsync(n => n.Id == id, CancellationToken)).IsRead.Should().BeFalse();
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private static DateTimeOffset At(int hour, int minute) => new(Tuesday.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.Zero);

    private static string EndpointOf(long userId) => $"https://push.example.test/actions-{userId}";

    private static IEnumerable<(string Action, string Title)> Actions(JsonObject document) =>
        document["actions"]!.AsArray().Select(a => (a!["action"]!.GetValue<string>(), a["title"]!.GetValue<string>()));

    private ITestClientFactory CreatePushFactory(CapturingWebPushSender push) =>
        CreateFactory(
            TestRoles.AdminAndUser,
            configureServices: services =>
            {
                services.AddSingleton<IWebPushSender>(push);
                // A producer runs in a job, with nobody logged in; the fake user would narrow the dispatcher's reads to itself.
                services.AddScoped<ILoggedUserService, BackgroundLoggedUserService>();
            },
            configOverrides: PushOnEmailOff);

    private ITestClientFactory CreatePinnedFactory(DateTimeOffset now) =>
        CreateFactory(TestRoles.AdminAndUser, configureServices: services => services.AddSingleton<TimeProvider>(new FixedTimeProvider(now)));

    private async Task NotifyAsync(ITestClientFactory factory, NotificationRecipients recipients, NotificationType type,
        INotificationPayload payload, NotificationReminderOccurrence? occurrence = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<INotificationService>()
            .NotifyAsync(recipients, type, payload, reminderOccurrence: occurrence, ct: CancellationToken);
    }

    private async Task<HttpStatusCode> PostActionAsync(HttpClient client, long notificationId, string action) =>
        (await client.PostAsync($"api/notification/{notificationId}/action/{action}", null, CancellationToken)).StatusCode;

    private async Task<long> SeedNotificationAsync(long userId, NotificationType type, string payloadJson, long? reminderDefinitionId = null)
    {
        await using var db = CreateDbContext();
        var notification = new Notification
        {
            UserId = userId,
            Type = type,
            PayloadJson = payloadJson,
            ReminderDefinitionId = reminderDefinitionId,
            ReminderOccurrenceAt = reminderDefinitionId is null ? null : DateTimeOffset.UtcNow
        };
        db.Add(notification);
        await db.SaveChangesAsync(CancellationToken);
        return notification.Id;
    }

    private async Task<long> SeedBlockAsync(TimeOnly start, TimeOnly end, PlannerTaskStatus status = PlannerTaskStatus.NotStarted)
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"block-{Guid.NewGuid():N}", UserId, CancellationToken);
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, Tuesday, UserId, CancellationToken);
        return await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, start, end, UserId, status, ct: CancellationToken);
    }

    private async Task<PlannerTask> ReadTaskAsync(long taskId)
    {
        await using var db = CreateDbContext();
        return await db.Set<PlannerTask>().IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == taskId, CancellationToken);
    }

    private async Task<Notification> SingleTodoNotificationAsync()
    {
        await using var db = CreateDbContext();
        return await db.Set<Notification>().AsNoTracking()
            .SingleAsync(n => n.UserId == UserId && n.Type == TodoListsNotificationTypes.TodoItemDue, CancellationToken);
    }

    /// <summary>Through the endpoint, so the due reminder is registered as the app registers it.</summary>
    private async Task<long> CreateTodoItemAsync(HttpClient client, string name, DateOnly dueDate, TimeOnly dueTime)
    {
        long activityId, priorityId, listId;
        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, name, UserId, CancellationToken);
            priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 510, UserId, "push-action", CancellationToken);
            listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, UserId, "Push action list", ct: CancellationToken);
        }

        var response = await client.PostAsJsonAsync("api/todo-list-item", new
        {
            activityId,
            taskPriorityId = priorityId,
            todoListId = listId,
            dueDate,
            dueTime = new { hours = dueTime.Hour, minutes = dueTime.Minute }
        }, JsonOpts, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        return long.Parse(body);
    }

    private async Task FireScanAsync(ITestClientFactory factory, DateTimeOffset fireTime)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetServices<IScheduledJobHandler>()
            .Single(h => h.Key == ReminderScanJobHandler.HandlerKey)
            .ExecuteAsync(new ScheduledJobContext
            {
                ScheduledFireTimeUtc = fireTime.UtcDateTime,
                ActualFireTimeUtc = fireTime.UtcDateTime,
                JobKey = ReminderScanJobHandler.HandlerKey,
                CorrelationId = "push-action-test",
                TriggerSource = TriggerSource.Manual
            }, CancellationToken);
    }

    private static User NewUser(long id, AvailableLocales locale)
    {
        var email = $"push-action-{id}@test.com";
        return new User
        {
            Id = id,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            Locale = locale,
            Timezone = TimeZoneInfo.Utc
        };
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class BackgroundLoggedUserService : ILoggedUserService
    {
        public ClaimsPrincipal? LoggedUserPrincipal => null;
        public bool IsAuthenticated => false;
        public IEnumerable<string> GetRoles => [];
        public string GetEmail => throw new InvalidOperationException("No logged user in a background context.");
        public long GetUserId => throw new InvalidOperationException("No logged user in a background context.");
        public long? GetUserIdOrNull => null;
    }

    private sealed class CapturingWebPushSender : IWebPushSender
    {
        private readonly ConcurrentDictionary<string, string> _byEndpoint = new();

        public JsonObject DocumentFor(long userId) =>
            JsonNode.Parse(_byEndpoint[EndpointOf(userId)])!.AsObject();

        public Task<bool> SendAsync(PushTarget target, string payloadJson, CancellationToken ct = default)
        {
            _byEndpoint[target.Endpoint] = payloadJson;
            return Task.FromResult(false);
        }
    }
}
