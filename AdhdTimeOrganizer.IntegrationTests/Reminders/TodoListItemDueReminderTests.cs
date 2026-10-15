using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.application.eventHandler;
using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.IntegrationTests.Endpoints;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.TodoLists.application.notification;
using AdhdTimeOrganizer.TodoLists.application.service.reminder;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.reminders;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.Testing;
using Sydowwe.Notifications.domain.entity;
using Sydowwe.Notifications.infrastructure.email;
using Sydowwe.Reminders.application.job;
using Sydowwe.Reminders.domain.entity;
using Sydowwe.Reminders.domain.@enum;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Reminders;

/// <summary>
/// P1-02: a to-do with a due date <b>and</b> a due time is reminded at the user's lead before it, and never after it
/// is done or gone. Two halves, both asserted on rows:
/// <list type="bullet">
/// <item><b>Every write path syncs</b> — the <c>ReminderDefinition</c> exists, moved or was cancelled after each
/// endpoint and host path that changes an item's due instant, completion or existence. A path that forgets is
/// silent: nothing fails, the reminder just stays on the user's upcoming list.</item>
/// <item><b>The dispatch guard is the net under them</b> — an item finished, moved or cascaded away behind the
/// sync's back is retired at fire time (<c>Skipped / SubjectRetired</c>) and no <c>Notification</c> row is
/// written.</item>
/// </list>
/// Real clock throughout, with due dates days ahead so a run never straddles one; the scan is driven from a
/// chosen fire instant, as in <see cref="ReminderRegistrationTests"/>. The fixture user is on UTC.
/// <para><b>Rank band 500</b>, see <c>FilterSortTodoListTests</c> for why every file claims one.</para>
/// </summary>
[Collection("Postgres")]
public class TodoListItemDueReminderTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    /// <summary>The fixture sets every MAIL_* var; TodoItemDue is email-off by default, this pins the switch too.</summary>
    private static readonly KeyValuePair<string, string?>[] EmailOff =
        [new($"{EmailNotificationOptions.SectionName}:Enabled", "false")];

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static DateOnly DueDay => Today.AddDays(3);
    private static readonly TimeOnly Noon = new(12, 0);

    private static DateTimeOffset Utc(DateOnly date, TimeOnly time) => new(date.ToDateTime(time), TimeSpan.Zero);

    private long _priorityId;
    private long _listId;
    private long _otherListId;

    protected override async Task SeedAsync(DbContext db)
    {
        _priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 500, UserId, "due-reminder", CancellationToken);
        _listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, UserId, "Due reminder list", ct: CancellationToken);
        _otherListId = await TodoListTestSeedHelper.SeedTodoListAsync(db, UserId, "Other due reminder list", ct: CancellationToken);
    }

    // ---- registration on each write path -------------------------------------------------------------

    [Fact(DisplayName = "P1-02: creating a timed to-do registers one reminder at the default 30-minute lead, for its owner")]
    public async Task Create_TimedItem_RegistersAtTheDefaultLead()
    {
        var itemId = await CreateItemAsync(CreateClient(), "write the report", DueDay, Noon);

        var definition = await DefinitionAsync(itemId);
        definition.Should().NotBeNull();
        definition!.Status.Should().Be(ReminderStatus.Active);
        definition.DueAt.Should().Be(Utc(DueDay, Noon));
        definition.LeadOffsets.Select(o => o.OffsetMinutes).Should().Equal(-30);
        definition.NextOccurrenceAt.Should().Be(Utc(DueDay, Noon).AddMinutes(-30));
        definition.RecipientMode.Should().Be(RecipientMode.ExplicitUsers,
            "a resolver-strategy reminder would vanish from my-upcoming and could not be snoozed");
        definition.Recipients.Select(r => r.UserId).Should().Equal(UserId);
        definition.NotificationType.Should().Be(TodoListsNotificationTypes.TodoItemDue);
        definition.TemplateKey.Should().Be(TodoListItemDueReminder.TemplateKey);
    }

    [Fact(DisplayName = "P1-02: a date-only to-do gets no reminder — there is no instant to fire at")]
    public async Task Create_DateOnlyItem_RegistersNothing()
    {
        var itemId = await CreateItemAsync(CreateClient(), "someday this week", DueDay, null);

        (await DefinitionAsync(itemId)).Should().BeNull();
    }

    [Fact(DisplayName = "P1-02: a to-do created inside its lead window is reminded at the due time instead of not at all")]
    public async Task Create_InsideTheLeadWindow_FiresAtTheDueTime()
    {
        var due = DateTime.UtcNow.AddMinutes(12);
        var itemId = await CreateItemAsync(CreateClient(), "leave for the bus",
            DateOnly.FromDateTime(due), new TimeOnly(due.Hour, due.Minute));

        var definition = await DefinitionAsync(itemId);
        definition!.Status.Should().Be(ReminderStatus.Active);
        definition.LeadOffsets.Select(o => o.OffsetMinutes).Should().Equal(0);
    }

    [Fact(DisplayName = "P1-02: a to-do whose due time has already passed gets no reminder")]
    public async Task Create_AlreadyDue_RegistersNothing()
    {
        var itemId = await CreateItemAsync(CreateClient(), "yesterday's call", Today.AddDays(-1), Noon);

        (await DefinitionAsync(itemId)).Should().BeNull();
    }

    [Fact(DisplayName = "P1-02: editing the due time moves the one reminder rather than adding a second")]
    public async Task Update_MovesTheReminder()
    {
        var client = CreateClient();
        var itemId = await CreateItemAsync(client, "dentist", DueDay, Noon);

        await UpdateItemAsync(client, itemId, DueDay.AddDays(1), new TimeOnly(15, 30), isDone: false);

        var definitions = await AllDefinitionsAsync(itemId);
        definitions.Should().ContainSingle();
        definitions[0].Status.Should().Be(ReminderStatus.Active);
        definitions[0].DueAt.Should().Be(Utc(DueDay.AddDays(1), new TimeOnly(15, 30)));
    }

    [Fact(DisplayName = "P1-02: clearing the due time withdraws the reminder")]
    public async Task Update_ClearingTheTime_Cancels()
    {
        var client = CreateClient();
        var itemId = await CreateItemAsync(client, "pay rent", DueDay, Noon);

        await UpdateItemAsync(client, itemId, DueDay, null, isDone: false);

        await AssertCancelledAsync(itemId);
    }

    [Fact(DisplayName = "P1-02: ticking an item off in the PUT withdraws the reminder, un-ticking it brings it back")]
    public async Task Update_Completion_CancelsAndRestores()
    {
        var client = CreateClient();
        var itemId = await CreateItemAsync(client, "send invoice", DueDay, Noon);

        await UpdateItemAsync(client, itemId, DueDay, Noon, isDone: true);
        await AssertCancelledAsync(itemId);

        await UpdateItemAsync(client, itemId, DueDay, Noon, isDone: false);
        (await DefinitionAsync(itemId))!.Status.Should().Be(ReminderStatus.Active);
    }

    [Fact(DisplayName = "P1-02: the toggle withdraws the reminder of every item it ticks, and un-ticking restores it")]
    public async Task Toggle_CancelsAndRestores()
    {
        var client = CreateClient();
        var first = await CreateItemAsync(client, "first", DueDay, Noon);
        var second = await CreateItemAsync(client, "second", DueDay, new TimeOnly(14, 0));

        await ToggleAsync(client, [first, second], forceValue: true);
        await AssertCancelledAsync(first);
        await AssertCancelledAsync(second);

        await ToggleAsync(client, [first], forceValue: false);
        (await DefinitionAsync(first))!.Status.Should().Be(ReminderStatus.Active);
        await AssertCancelledAsync(second);
    }

    [Fact(DisplayName = "P1-02: ticking the last step completes the item and withdraws its reminder")]
    public async Task StepToggle_LastStep_Cancels()
    {
        var client = CreateClient();
        var itemId = await CreateItemAsync(client, "pack", DueDay, Noon,
            steps: [new { name = "only step", order = 0 }]);

        Guid stepId;
        await using (var db = CreateDbContext())
            stepId = (await db.Set<TodoListItem>().AsNoTracking().Include(i => i.Steps)
                .SingleAsync(i => i.Id == itemId, CancellationToken)).Steps.Single().Id;

        var response = await client.PatchAsync($"api/todo-list-item/{itemId}/steps/{stepId}/toggle",
            new StringContent(string.Empty), CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await AssertCancelledAsync(itemId);
    }

    [Fact(DisplayName = "P1-02: moving an item to another list keeps its reminder and re-registers the list it names")]
    public async Task Move_RefreshesThePayload()
    {
        var client = CreateClient();
        var itemId = await CreateItemAsync(client, "move me", DueDay, Noon);

        var response = await client.PatchAsJsonAsync($"api/todo-list-item/{itemId}/move",
            new { destinationListId = _otherListId }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(CancellationToken));

        var definition = await DefinitionAsync(itemId);
        definition!.Status.Should().Be(ReminderStatus.Active);
        JsonDocument.Parse(definition.PayloadJson).RootElement.GetProperty("todoListId").GetInt64().Should().Be(_otherListId);
    }

    [Fact(DisplayName = "P1-02: deleting an item withdraws its reminder")]
    public async Task Delete_Cancels()
    {
        var client = CreateClient();
        var itemId = await CreateItemAsync(client, "delete me", DueDay, Noon);

        (await client.DeleteAsync($"api/todo-list-item/{itemId}", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await AssertCancelledAsync(itemId);
    }

    [Fact(DisplayName = "P1-02: deleting a list cancels the reminder of every item the database cascade removes")]
    public async Task DeleteList_CancelsEveryCascadedItem()
    {
        var client = CreateClient();
        var first = await CreateItemAsync(client, "first", DueDay, Noon);
        var second = await CreateItemAsync(client, "second", DueDay, new TimeOnly(16, 0));
        var elsewhere = await CreateItemAsync(client, "elsewhere", DueDay, Noon, listId: _otherListId);

        (await client.DeleteAsync($"api/todo-list/{_listId}", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await AssertCancelledAsync(first);
        await AssertCancelledAsync(second);
        (await DefinitionAsync(elsewhere))!.Status.Should().Be(ReminderStatus.Active, "only the deleted list's items went");
    }

    [Fact(DisplayName = "P1-02: the bulk overdue reschedule registers every moved timed item, not just the first")]
    public async Task RescheduleOverdue_RegistersEveryMovedItem()
    {
        var first = await SeedItemAsync("overdue-1", Today.AddDays(-2), new TimeOnly(9, 0));
        var second = await SeedItemAsync("overdue-2", Today.AddDays(-5), new TimeOnly(17, 45));
        var dateOnly = await SeedItemAsync("overdue-date-only", Today.AddDays(-1), null);

        var response = await CreateClient().PostAsJsonAsync("api/todo-list-item/reschedule-overdue",
            new { listId = _listId, days = 2 }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(CancellationToken));

        (await DefinitionAsync(first))!.DueAt.Should().Be(Utc(Today.AddDays(2), new TimeOnly(9, 0)));
        (await DefinitionAsync(second))!.DueAt.Should().Be(Utc(Today.AddDays(2), new TimeOnly(17, 45)));
        (await DefinitionAsync(dateOnly)).Should().BeNull();
    }

    [Fact(DisplayName = "P1-02: the lead setting defaults to 30, and changing it re-registers every open timed item")]
    public async Task Settings_ChangeReRegistersAtTheNewLead()
    {
        var client = CreateClient();
        var itemId = await CreateItemAsync(client, "lead", DueDay, Noon);

        var get = await client.GetFromJsonAsync<JsonElement>("api/todo-list/settings", JsonOpts, CancellationToken);
        get.GetProperty("dueReminderMinutesBefore").GetInt32().Should().Be(30);

        (await client.PutAsJsonAsync("api/todo-list/settings", new { dueReminderMinutesBefore = 90 }, JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DefinitionAsync(itemId))!.LeadOffsets.Select(o => o.OffsetMinutes).Should().Equal(-90);

        (await client.PutAsJsonAsync("api/todo-list/settings", new { dueReminderMinutesBefore = 0 }, JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DefinitionAsync(itemId))!.LeadOffsets.Select(o => o.OffsetMinutes).Should().Equal(new[] { 0 },
            "0 is a real value — 'at the due time' — and must not be swallowed as 'unset'");

        (await client.PutAsJsonAsync("api/todo-list/settings", new { dueReminderMinutesBefore = 24 * 60 + 1 }, JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "P1-02: changing the user's time zone re-registers each reminder at the instant in the new zone")]
    public async Task TimeZoneChange_ReRegisters()
    {
        var client = CreateClient();
        var itemId = await CreateItemAsync(client, "zone", DueDay, Noon);

        var response = await client.PutAsJsonAsync("api/user/preferences", new { timezone = "Europe/Bratislava" }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(CancellationToken));

        var bratislava = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bratislava");
        var expected = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DueDay.ToDateTime(Noon), bratislava));
        (await DefinitionAsync(itemId))!.DueAt.Should().Be(expected);
    }

    [Fact(DisplayName = "P1-02: a linked planner block ticked off finishes the to-do and withdraws its reminder; un-ticked, it comes back")]
    public async Task PlannerTaskCompletion_CancelsAndRestores()
    {
        var itemId = await CreateItemAsync(CreateClient(), "planned", DueDay, Noon);
        long activityId;
        await using (var db = CreateDbContext())
            activityId = (await db.Set<TodoListItem>().AsNoTracking().SingleAsync(i => i.Id == itemId, CancellationToken)).ActivityId;

        var handler = new PlannerTaskIsDoneChangedEventHandler(
            Fixture.UnauthenticatedFactory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PlannerTaskIsDoneChangedEventHandler>.Instance);

        await handler.HandleAsync(new PlannerTaskIsDoneChangedEvent(activityId, UserId, true, itemId), CancellationToken);
        await AssertCancelledAsync(itemId);

        await handler.HandleAsync(new PlannerTaskIsDoneChangedEvent(activityId, UserId, false, itemId), CancellationToken);
        (await DefinitionAsync(itemId))!.Status.Should().Be(ReminderStatus.Active);
    }

    [Fact(DisplayName = "P1-02: a to-do completed by tracked time withdraws its reminder")]
    public async Task TrackedTimeCompletion_Cancels()
    {
        var itemId = await CreateItemAsync(CreateClient(), "tracked", DueDay, Noon);
        long activityId;
        await using (var db = CreateDbContext())
        {
            await db.Set<TodoListItem>().Where(i => i.Id == itemId)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.SuggestedTime, new IntTime(0, 10)), CancellationToken);
            activityId = (await db.Set<TodoListItem>().AsNoTracking().SingleAsync(i => i.Id == itemId, CancellationToken)).ActivityId;
        }

        var handler = new ActivityTimeRecordedEventHandler(
            Fixture.UnauthenticatedFactory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ActivityTimeRecordedEventHandler>.Instance);
        await handler.HandleAsync(new ActivityTimeRecordedEvent(UserId, Today, new TimeOnly(12, 0), new TimeOnly(12, 1),
            [new ActivityDayTotal(activityId, 600, itemId)]), CancellationToken);

        await using (var db = CreateDbContext())
            (await db.Set<TodoListItem>().AsNoTracking().SingleAsync(i => i.Id == itemId, CancellationToken)).IsDone
                .Should().BeTrue("the precondition: the tracked time did complete the item");
        await AssertCancelledAsync(itemId);
    }

    // ---- the scan: delivery and the guard ------------------------------------------------------------

    [Fact(DisplayName = "P1-02: at the lead time the scan pushes a TodoItemDue notification to the item's owner")]
    public async Task Scan_Fires()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var itemId = await CreateItemAsync(factory.CreateClient(), "file taxes", DueDay, Noon);

        await FireScanAsync(factory, Utc(DueDay, Noon).AddMinutes(-29));

        var definition = await DefinitionAsync(itemId);
        await using var db = CreateDbContext();
        var dispatch = await db.Set<ReminderDispatch>().AsNoTracking().SingleAsync(d => d.ReminderDefinitionId == definition!.Id, CancellationToken);
        dispatch.Outcome.Should().Be(DispatchOutcome.Sent);

        var notification = await db.Set<Notification>().AsNoTracking()
            .SingleAsync(n => n.UserId == UserId && n.Type == TodoListsNotificationTypes.TodoItemDue, CancellationToken);
        var payload = JsonDocument.Parse(notification.PayloadJson).RootElement;
        payload.GetProperty("todoListItemId").GetInt64().Should().Be(itemId);
        payload.GetProperty("minutesBefore").GetInt32().Should().Be(30);
    }

    [Fact(DisplayName = "P1-02: an item ticked off behind the sync's back is retired at fire time — no notification")]
    public async Task Scan_DoneBehindTheSyncsBack_IsRetired()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var itemId = await CreateItemAsync(factory.CreateClient(), "done elsewhere", DueDay, Noon);

        await using (var db = CreateDbContext())
            await db.Set<TodoListItem>().Where(i => i.Id == itemId)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.IsDone, true), CancellationToken);

        await FireScanAsync(factory, Utc(DueDay, Noon).AddMinutes(-29));

        await AssertRetiredAsync(itemId);
    }

    [Fact(DisplayName = "P1-02: an item cascaded away with its list behind the sync's back is retired — no notification")]
    public async Task Scan_ListCascadedAway_IsRetired()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var itemId = await CreateItemAsync(factory.CreateClient(), "cascaded", DueDay, Noon);

        await using (var db = CreateDbContext())
            await db.Set<TodoList>().Where(l => l.Id == _listId).ExecuteDeleteAsync(CancellationToken);

        await FireScanAsync(factory, Utc(DueDay, Noon).AddMinutes(-29));

        await AssertRetiredAsync(itemId);
    }

    [Fact(DisplayName = "P1-02: an item moved to another time behind the sync's back is retired — the stale instant never rings")]
    public async Task Scan_MovedBehindTheSyncsBack_IsRetired()
    {
        await using var factory = CreateFactory(TestRoles.AdminAndUser, configOverrides: EmailOff);
        var itemId = await CreateItemAsync(factory.CreateClient(), "moved", DueDay, Noon);

        await using (var db = CreateDbContext())
            await db.Set<TodoListItem>().Where(i => i.Id == itemId)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.DueTime, new TimeOnly(18, 0)), CancellationToken);

        await FireScanAsync(factory, Utc(DueDay, Noon).AddMinutes(-29));

        await AssertRetiredAsync(itemId);
    }

    // ---- the user's surfaces -------------------------------------------------------------------------

    [Fact(DisplayName = "P1-02: the reminder is on the user's own upcoming list, labelled with the item's title")]
    public async Task MyUpcoming_ListsTheReminder_ByTitle()
    {
        var client = CreateUserRoleClient();
        var itemId = await CreateItemAsync(client, "labelled item", DueDay, Noon);
        var definitionId = (await DefinitionAsync(itemId))!.Id;

        var response = await client.PostAsJsonAsync("api/reminder-dashboard/my-upcoming", new
        {
            useFilter = true,
            filter = new { ownerModule = TodoListItemDueReminder.OwnerModule },
            sortBy = Array.Empty<object>(),
            itemsPerPage = 200,
            page = 1
        }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var row = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken))
            .GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetInt64() == definitionId);
        row.GetProperty("displayLabel").GetString().Should().StartWith("labelled item");
    }

    [Fact(DisplayName = "P1-02: to-do reminders can be muted as a kind before one exists")]
    public async Task KindCatalog_OffersTheKind()
    {
        var response = await CreateClient().GetAsync("api/reminder-preference", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var kinds = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken)).GetProperty("kinds");
        kinds.EnumerateArray().Should().Contain(k =>
            k.GetProperty("ownerModule").GetString() == TodoListItemDueReminder.OwnerModule
            && k.GetProperty("kind").GetString() == TodoListItemDueReminder.Kind
            && k.GetProperty("enabled").GetBoolean());
    }

    // ---- helpers -------------------------------------------------------------------------------------

    /// <summary>One activity per item: <c>todo_list_item</c> is unique on <c>(UserId, ActivityId, TodoListId)</c>.</summary>
    private async Task<long> CreateItemAsync(HttpClient client, string name, DateOnly? dueDate, TimeOnly? dueTime,
        long? listId = null, object[]? steps = null)
    {
        long activityId;
        await using (var db = CreateDbContext())
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, name, UserId, CancellationToken);

        var response = await client.PostAsJsonAsync("api/todo-list-item", new
        {
            activityId,
            taskPriorityId = _priorityId,
            todoListId = listId ?? _listId,
            dueDate,
            dueTime = dueTime is { } t ? new { hours = t.Hour, minutes = t.Minute } : null,
            steps
        }, JsonOpts, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        return long.Parse(body);
    }

    /// <summary>Straight into the table — no endpoint, so no reminder: the starting state of a bulk path.</summary>
    private async Task<long> SeedItemAsync(string name, DateOnly dueDate, TimeOnly? dueTime)
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, name, UserId, CancellationToken);
        var itemId = await TodoListTestSeedHelper.SeedTodoListItemAsync(
            db, activityId, _priorityId, _listId, UserId, ct: CancellationToken, dueDate: dueDate);
        await db.Set<TodoListItem>().Where(i => i.Id == itemId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.DueTime, dueTime), CancellationToken);
        return itemId;
    }

    private async Task UpdateItemAsync(HttpClient client, long itemId, DateOnly? dueDate, TimeOnly? dueTime, bool isDone)
    {
        TodoListItem item;
        await using (var db = CreateDbContext())
            item = await db.Set<TodoListItem>().AsNoTracking().SingleAsync(i => i.Id == itemId, CancellationToken);

        var response = await client.PutAsJsonAsync($"api/todo-list-item/{itemId}", new
        {
            activityId = item.ActivityId,
            taskPriorityId = item.TaskPriorityId,
            isDone,
            dueDate,
            dueTime = dueTime is { } t ? new { hours = t.Hour, minutes = t.Minute } : null
        }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private async Task ToggleAsync(HttpClient client, long[] ids, bool forceValue)
    {
        var response = await client.PatchAsJsonAsync("api/todo-list-item/toggle-is-done",
            new { ids, forceValue }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private async Task AssertCancelledAsync(long itemId)
    {
        var definition = await DefinitionAsync(itemId);
        definition.Should().NotBeNull("it was registered first");
        definition!.Status.Should().Be(ReminderStatus.Cancelled);
        definition.NextOccurrenceAt.Should().BeNull();
    }

    /// <summary>The guard's row, and — the point — nothing in the bell.</summary>
    private async Task AssertRetiredAsync(long itemId)
    {
        var definition = await DefinitionAsync(itemId);
        await using var db = CreateDbContext();
        var dispatch = await db.Set<ReminderDispatch>().AsNoTracking().SingleAsync(d => d.ReminderDefinitionId == definition!.Id, CancellationToken);
        dispatch.Outcome.Should().Be(DispatchOutcome.Skipped);
        dispatch.SkipReason.Should().Be(SkipReason.SubjectRetired);

        (await db.Set<Notification>().AnyAsync(n => n.Type == TodoListsNotificationTypes.TodoItemDue, CancellationToken))
            .Should().BeFalse("a retired occurrence sends nothing");
    }

    private async Task<ReminderDefinition?> DefinitionAsync(long itemId) =>
        (await AllDefinitionsAsync(itemId)).SingleOrDefault();

    private async Task<List<ReminderDefinition>> AllDefinitionsAsync(long itemId)
    {
        await using var db = CreateDbContext();
        return await db.Set<ReminderDefinition>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(d => d.LeadOffsets)
            .Include(d => d.Recipients)
            .Where(d => d.OwnerModule == TodoListItemDueReminder.OwnerModule
                        && d.SubjectType == TodoListItemDueReminder.SubjectType
                        && d.SubjectId == itemId.ToString()
                        && d.Kind == TodoListItemDueReminder.Kind)
            .ToListAsync(CancellationToken);
    }

    /// <summary>The scan, resolved by key as the Scheduler's dispatcher finds it, fired at a chosen instant.</summary>
    private async Task FireScanAsync(ITestClientFactory factory, DateTimeOffset fireTime)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var scan = scope.ServiceProvider.GetServices<IScheduledJobHandler>()
            .Single(h => h.Key == ReminderScanJobHandler.HandlerKey);

        await scan.ExecuteAsync(new ScheduledJobContext
        {
            ScheduledFireTimeUtc = fireTime.UtcDateTime,
            ActualFireTimeUtc = fireTime.UtcDateTime,
            JobKey = ReminderScanJobHandler.HandlerKey,
            CorrelationId = "todo-due-reminder-test",
            TriggerSource = TriggerSource.Manual
        }, CancellationToken);
    }
}
