using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Endpoints;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.TodoLists.application.service.reminder;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Sydowwe.Reminders.domain.entity;
using Sydowwe.Reminders.domain.@enum;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Reminders;

/// <summary>
/// P1-02 follow-up: what the activity's own lifecycle — archive, restore, delete, deleting its role — does to the
/// due reminders of the to-dos on it. None of those writes passes through the to-do slice.
/// <para><b>Rank band 510</b>, see <c>FilterSortTodoListTests</c> for why every file claims one.</para>
/// </summary>
[Collection("Postgres")]
public class TodoListItemDueReminderActivityLifecycleTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private static DateOnly DueDay => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
    private static readonly TimeOnly Noon = new(12, 0);

    private long _priorityId;
    private long _listId;

    protected override async Task SeedAsync(DbContext db)
    {
        _priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 510, UserId, "due-reminder-lifecycle", CancellationToken);
        _listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, UserId, "Lifecycle list", ct: CancellationToken);
    }

    [Fact(DisplayName = "Precondition: a to-do whose activity is archived drops out of its list's read")]
    public async Task ArchivedActivity_HidesItsTodos()
    {
        var client = CreateClient();
        var (itemId, activityId) = await CreateItemAsync(client, "archived-visibility");
        (await ListItemIdsAsync(client)).Should().Contain(itemId);

        await SetArchivedAsync(client, activityId, true);

        (await ListItemIdsAsync(client)).Should().NotContain(itemId);
    }

    [Fact(DisplayName = "P1-02: archiving an activity withdraws its to-dos' reminders, restoring it brings them back")]
    public async Task Archive_Cancels_Restore_ReRegisters()
    {
        var client = CreateClient();
        var (itemId, activityId) = await CreateItemAsync(client, "archive-me");
        var (otherItemId, _) = await CreateItemAsync(client, "untouched");

        await SetArchivedAsync(client, activityId, true);
        await AssertStatusAsync(itemId, ReminderStatus.Cancelled);
        await AssertStatusAsync(otherItemId, ReminderStatus.Active);

        await SetArchivedAsync(client, activityId, false);
        await AssertStatusAsync(itemId, ReminderStatus.Active);
    }

    [Fact(DisplayName = "P1-02: deleting an activity cancels the reminders of the to-dos its cascade removes")]
    public async Task DeleteActivity_CancelsCascadedItems()
    {
        var client = CreateClient();
        var (itemId, activityId) = await CreateItemAsync(client, "delete-activity");
        var (otherItemId, _) = await CreateItemAsync(client, "keep-activity");

        (await client.DeleteAsync($"api/activity/{activityId}", CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var db = CreateDbContext())
            (await db.Set<TodoListItem>().AnyAsync(i => i.Id == itemId, CancellationToken)).Should().BeFalse("the precondition: the cascade took it");
        await AssertStatusAsync(itemId, ReminderStatus.Cancelled);
        await AssertStatusAsync(otherItemId, ReminderStatus.Active);
    }

    [Fact(DisplayName = "P1-02: a merge cancels a collapsed duplicate's reminder and relabels a repointed item's under the survivor")]
    public async Task Merge_CancelsCollapsed_ReRegistersRepointed()
    {
        var client = CreateClient();
        var (_, survivorId) = await CreateItemAsync(client, "survivor");
        // Same list as the survivor's item: the repoint would collide on (UserId, ActivityId, TodoListId), so it collapses.
        var (collapsedId, mergedId) = await CreateItemAsync(client, "duplicate");
        // Another list: it is repointed and survives.
        long otherListId;
        await using (var db = CreateDbContext())
            otherListId = await TodoListTestSeedHelper.SeedTodoListAsync(db, UserId, "Lifecycle other list", ct: CancellationToken);
        var repointedId = await CreateItemOnActivityAsync(client, mergedId, otherListId);

        var response = await client.PostAsJsonAsync("api/activity/merge",
            new { survivorId, mergedIds = new[] { mergedId } }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(CancellationToken));

        await AssertStatusAsync(collapsedId, ReminderStatus.Cancelled);

        var repointed = await DefinitionAsync(repointedId);
        repointed!.Status.Should().Be(ReminderStatus.Active);
        JsonDocument.Parse(repointed.PayloadJson).RootElement.GetProperty("title").GetString()
            .Should().StartWith("survivor", "the reminder follows the item onto the survivor, and so does its label");
    }

    // ---- helpers -------------------------------------------------------------------------------------

    private async Task<long> CreateItemOnActivityAsync(HttpClient client, long activityId, long listId)
    {
        var response = await client.PostAsJsonAsync("api/todo-list-item", new
        {
            activityId,
            taskPriorityId = _priorityId,
            todoListId = listId,
            dueDate = DueDay,
            dueTime = new { hours = Noon.Hour, minutes = Noon.Minute }
        }, JsonOpts, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        return long.Parse(body);
    }

    private async Task AssertStatusAsync(long itemId, ReminderStatus expected)
    {
        var definition = await DefinitionAsync(itemId);
        definition.Should().NotBeNull("every item here was created timed, so it was registered");
        definition!.Status.Should().Be(expected);
    }

    private async Task<ReminderDefinition?> DefinitionAsync(long itemId)
    {
        await using var db = CreateDbContext();
        return await db.Set<ReminderDefinition>().AsNoTracking().IgnoreQueryFilters()
            .SingleOrDefaultAsync(d => d.OwnerModule == TodoListItemDueReminder.OwnerModule
                                       && d.SubjectId == itemId.ToString()
                                       && d.Kind == TodoListItemDueReminder.Kind, CancellationToken);
    }

    private async Task<(long ItemId, long ActivityId)> CreateItemAsync(HttpClient client, string name)
    {
        long activityId;
        await using (var db = CreateDbContext())
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, name, UserId, CancellationToken);

        var response = await client.PostAsJsonAsync("api/todo-list-item", new
        {
            activityId,
            taskPriorityId = _priorityId,
            todoListId = _listId,
            dueDate = DueDay,
            dueTime = new { hours = Noon.Hour, minutes = Noon.Minute }
        }, JsonOpts, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        return (long.Parse(body), activityId);
    }

    private async Task SetArchivedAsync(HttpClient client, long activityId, bool isArchived)
    {
        var response = await client.PatchAsJsonAsync($"api/activity/{activityId}/archived", new { isArchived }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private async Task<List<long>> ListItemIdsAsync(HttpClient client)
    {
        var response = await client.GetAsync($"api/todo-list-item?todoListId={_listId}", CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).ToList();
    }
}
