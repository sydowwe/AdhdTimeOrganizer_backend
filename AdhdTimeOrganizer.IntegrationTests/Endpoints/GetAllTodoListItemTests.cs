using System.Net;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// <c>PERF-1</c> (<c>review/todo-lists/02-findings.md</c>) — <c>GET /api/todo-list-item</c> is bounded to one
/// list. <c>TodoListId</c> used to be optional, and omitting it returned every item the caller owned, each row
/// carrying the whole <c>Activity → Role → Category</c> graph plus its owned steps JSON.
/// <para>
/// The 400 is the point of the first test: a route that silently defaulted to "all lists" would be the same
/// unbounded read with nothing naming it, and nothing in the response would say so. The frontend's only
/// unfiltered caller — the planner's activity picker with its list filter cleared — now asks per list.
/// </para>
/// <para>
/// <b>Rank band 390–399</b>, see <c>FilterSortTodoListTests</c> for why every file claims one.
/// </para>
/// </summary>
[Collection("Postgres")]
public class GetAllTodoListItemTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private const string ItemRoute = "/api/todo-list-item";

    private long _listId;
    private long _otherListId;

    protected override async Task SeedAsync(DbContext db)
    {
        var priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 390, UserId, "read-scope", CancellationToken);
        _listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, UserId, "Wanted list", ct: CancellationToken);
        _otherListId = await TodoListTestSeedHelper.SeedTodoListAsync(db, UserId, "Other list", ct: CancellationToken);

        // One activity per item: todo_list_item is unique on (UserId, ActivityId, TodoListId).
        var wantedActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "wanted-item", UserId, CancellationToken);
        var topActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "wanted-item-top", UserId, CancellationToken);
        var otherActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "other-item", UserId, CancellationToken);

        // Seeded in the opposite order to the one they must come back in, so "ordered by DisplayOrder" cannot
        // be confused with "ordered by id".
        await TodoListTestSeedHelper.SeedTodoListItemAsync(db, wantedActivityId, priorityId, _listId, UserId, displayOrder: 2000, ct: CancellationToken);
        await TodoListTestSeedHelper.SeedTodoListItemAsync(db, topActivityId, priorityId, _listId, UserId, displayOrder: 500, ct: CancellationToken);
        await TodoListTestSeedHelper.SeedTodoListItemAsync(db, otherActivityId, priorityId, _otherListId, UserId, ct: CancellationToken);
    }

    [Fact(DisplayName = "PERF-1: the unfiltered read is a 400, not every item the caller owns")]
    public async Task WithoutTodoListId_Returns400()
    {
        var response = await CreateClient().GetAsync(ItemRoute, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "TodoListId is required -- an absent one must not fall back to reading every list");
    }

    [Fact(DisplayName = "PERF-1: a non-positive TodoListId is a 400 rather than an empty page")]
    public async Task WithZeroTodoListId_Returns400()
    {
        var response = await CreateClient().GetAsync($"{ItemRoute}?todoListId=0", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// The positive control. Without it, a validator that rejected <i>every</i> request would pass the two
    /// cases above; and the second assertion is what proves the filter still filters rather than the whole
    /// read having been narrowed to nothing.
    /// </summary>
    [Fact(DisplayName = "PERF-1: with a TodoListId, only that list's items come back")]
    public async Task WithTodoListId_ReturnsOnlyThatListsItems()
    {
        var items = await ReadAsync(_listId);

        items.Select(i => i.Activity.Name).Should().BeEquivalentTo(["wanted-item", "wanted-item-top"]);

        var others = await ReadAsync(_otherListId);
        others.Should().ContainSingle().Which.Activity.Name.Should().Be("other-item");
    }

    /// <summary>
    /// <c>TEST-4</c>'s other half. The order is the to-do view's rendering order, and it comes from the
    /// endpoint's <c>OrderBy(DisplayOrder)</c> alone — nothing downstream re-sorts, so dropping that clause
    /// would answer 200 with the list silently rearranged.
    /// </summary>
    [Fact(DisplayName = "TEST-4: items come back ordered by DisplayOrder, not by id")]
    public async Task Items_AreOrderedByDisplayOrder()
    {
        var items = await ReadAsync(_listId);

        items.Select(i => i.Activity.Name).Should().Equal("wanted-item-top", "wanted-item");
        items.Select(i => i.DisplayOrder).Should().BeInAscendingOrder();
    }

    /// <summary>
    /// The list id is not ownership-checked, deliberately — the query is scoped to the caller's items, so
    /// another user's list id is an empty page rather than a leak. This is what makes the missing check safe
    /// to leave out, so it is pinned rather than assumed.
    /// </summary>
    [Fact(DisplayName = "PERF-1: another user's list id returns an empty page, not their items")]
    public async Task WithAnotherUsersListId_ReturnsEmpty()
    {
        long theirListId;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            var theirActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "theirs-item", ReminderSeedHelper.OtherUserId, CancellationToken);
            var theirPriorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 391, ReminderSeedHelper.OtherUserId, "theirs", CancellationToken);
            theirListId = await TodoListTestSeedHelper.SeedTodoListAsync(db, ReminderSeedHelper.OtherUserId, "Theirs list", ct: CancellationToken);
            await TodoListTestSeedHelper.SeedTodoListItemAsync(db, theirActivityId, theirPriorityId, theirListId, ReminderSeedHelper.OtherUserId, ct: CancellationToken);
        }

        var items = await ReadAsync(theirListId);

        items.Should().BeEmpty();
    }

    private async Task<List<TodoListItemResponse>> ReadAsync(long todoListId)
    {
        var response = await CreateClient().GetAsync($"{ItemRoute}?todoListId={todoListId}", CancellationToken);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        return JsonSerializer.Deserialize<List<TodoListItemResponse>>(body, JsonOpts)!;
    }
}
