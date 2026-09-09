using System.Net;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.valueObject;
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
/// <c>TEST-4</c>'s other half is the projection. <c>TodoListItemResponse.Projection</c> builds a five-level
/// graph by hand, and a branch dropped from it fails the way <c>TodoListItemPairingTests</c> describes: the
/// field reads back as <c>null</c>, which is indistinguishable from "not set". So every branch is asserted
/// with a non-default value, including the <c>Category == null</c> arm, which no other test reaches.
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
    /// <para>
    /// The second half is the one that matters if anyone "fixes" this: empty is only the right answer while
    /// the other user's rows are still <i>there</i>. A read that emptied the page by deleting or reassigning
    /// them would satisfy the first assertion perfectly.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "PERF-1: another user's list id returns an empty page, and leaves their items intact")]
    public async Task WithAnotherUsersListId_ReturnsEmpty()
    {
        long theirListId;
        long theirItemId;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            var theirActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "theirs-item", ReminderSeedHelper.OtherUserId, CancellationToken);
            var theirPriorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 391, ReminderSeedHelper.OtherUserId, "theirs", CancellationToken);
            theirListId = await TodoListTestSeedHelper.SeedTodoListAsync(db, ReminderSeedHelper.OtherUserId, "Theirs list", ct: CancellationToken);
            theirItemId = await TodoListTestSeedHelper.SeedTodoListItemAsync(db, theirActivityId, theirPriorityId, theirListId, ReminderSeedHelper.OtherUserId, ct: CancellationToken);
        }

        var items = await ReadAsync(theirListId);

        items.Should().BeEmpty();

        await using var verifyDb = CreateDbContext();
        var theirItem = await verifyDb.Set<TodoListItem>()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(i => i.Id == theirItemId, CancellationToken);

        theirItem.Should().NotBeNull("the read must answer empty by scoping, not by touching the other user's rows");
        theirItem!.UserId.Should().Be(ReminderSeedHelper.OtherUserId);
        theirItem.TodoListId.Should().Be(theirListId);
    }

    /// <summary>
    /// <c>TEST-4</c> §6 — one assertion per branch of the projection, every one of them against a value that
    /// is not the type's default. The step ids are the sharpest of these: <c>TodoListStep.Id</c> is
    /// <c>ValueGeneratedNever</c> and the step-toggle route addresses steps by that Guid, so a projection that
    /// dropped it would hand the frontend a list of steps it could never toggle.
    /// </summary>
    [Fact(DisplayName = "TEST-4: every branch of the item projection round-trips, including the populated Category arm")]
    public async Task Projection_CarriesTheWholeItemGraph()
    {
        var seeded = await SeedProjectionListAsync();

        var items = await ReadAsync(seeded.ListId);
        var item = items.Should().ContainSingle(i => i.Id == seeded.ItemId).Subject;

        item.Activity.Name.Should().Be("projection-activity");
        item.Activity.Text.Should().Be("activity note");
        item.Activity.IsUnavoidable.Should().BeTrue();

        item.Activity.Role.Name.Should().Be("projection-role");
        item.Activity.Role.Text.Should().Be("role note");
        item.Activity.Role.Color.Should().Be("#010203");
        item.Activity.Role.Icon.Should().Be("mdi-role");

        item.Activity.Category.Should().NotBeNull();
        item.Activity.Category!.Name.Should().Be("projection-category");
        item.Activity.Category.Text.Should().Be("category note");
        item.Activity.Category.Color.Should().Be("#040506");
        item.Activity.Category.Icon.Should().Be("mdi-category");

        item.TaskPriority.Text.Should().Be("projection-priority");
        item.TaskPriority.Color.Should().Be("#abcdef");
        item.TaskPriority.Priority.Should().Be(392);

        item.Steps.Should().HaveCount(2);
        item.Steps.Select(s => s.Id).Should().BeEquivalentTo([seeded.FirstStepId, seeded.SecondStepId],
            "TodoListStep.Id is ValueGeneratedNever and the step-toggle route addresses steps by it");
        var firstStep = item.Steps.Single(s => s.Id == seeded.FirstStepId);
        firstStep.Name.Should().Be("step one");
        firstStep.Order.Should().Be(1);
        firstStep.IsDone.Should().BeTrue();
        firstStep.Note.Should().Be("step note");

        item.DoneCount.Should().Be(2);
        item.TotalCount.Should().Be(3);
        item.Note.Should().Be("item note");
        item.DueDate.Should().Be(new DateOnly(2026, 3, 14));
        item.DueTime.Should().Be(new TimeOnly(15, 9));
        item.IsDone.Should().BeTrue();
        item.DisplayOrder.Should().Be(4200);
        item.PairedLeisureActivityId.Should().Be(seeded.PairedActivityId);
    }

    /// <summary>
    /// The null arm of the projection's only conditional (<c>TodoListItemResponse.cs:36</c>). Nothing else
    /// covers it, and an activity without a category is the ordinary case — <c>CategoryId</c> is nullable and
    /// the create form does not require one.
    /// </summary>
    [Fact(DisplayName = "TEST-4: an activity with no category projects Category as null rather than failing")]
    public async Task Projection_HandlesTheNullCategoryArm()
    {
        var seeded = await SeedProjectionListAsync();

        var items = await ReadAsync(seeded.ListId);
        var item = items.Should().ContainSingle(i => i.Id == seeded.UncategorisedItemId).Subject;

        item.Activity.Category.Should().BeNull();
        item.Activity.Role.Should().NotBeNull("the role is required — a null one would break the projection, not degrade it");
    }

    /// <summary>
    /// A pin on today's shape, not a bug report. <c>SuggestedTime</c> is accepted on create and update,
    /// validated (<c>CreateTodoListItemValidator:38-41</c>) and persisted through
    /// <c>NullableIntTimeConverter</c> (<c>TodoListItemConfiguration.cs:19-20</c>) — and then never read
    /// back by any response. Whether that is a gap or intentional is a product question this review cannot
    /// settle; the assertion makes it visible, and turns adding the field into a deliberate edit here.
    /// <para>
    /// <c>CompletedTimestamp</c> and <c>TodoListId</c> are absent too, but deliberately: the daily recap owns
    /// the first, and the caller already knows the second — it is the parameter it just passed.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "TEST-4: SuggestedTime is accepted and stored but never projected — write-only today")]
    public async Task Projection_OmitsSuggestedTime()
    {
        var seeded = await SeedProjectionListAsync();

        await using (var verifyDb = CreateDbContext())
        {
            var stored = await verifyDb.Set<TodoListItem>()
                .IgnoreQueryFilters()
                .SingleAsync(i => i.Id == seeded.ItemId, CancellationToken);

            stored.SuggestedTime.Should().NotBeNull("the column is written — this test is about the read, not the write");
            stored.SuggestedTime!.Hours.Should().Be(1);
            stored.SuggestedTime.Minutes.Should().Be(30);
        }

        using var doc = JsonDocument.Parse(await ReadRawAsync(seeded.ListId));
        var element = doc.RootElement.EnumerateArray()
            .Single(e => e.GetProperty("id").GetInt64() == seeded.ItemId);

        element.TryGetProperty("suggestedTime", out _).Should().BeFalse();
        element.TryGetProperty("completedTimestamp", out _).Should().BeFalse();
        element.TryGetProperty("todoListId", out _).Should().BeFalse();
    }

    /// <summary>
    /// The role gate comes from the global endpoint configurator, not from this endpoint — it declares no
    /// <c>AllowedRoles</c> of its own — so the only thing a test can pin is the observed outcome: the plain
    /// User role is enough, and anonymous is challenged. A configurator change that dropped User would
    /// otherwise turn the main to-do view into a 403 with nothing failing here.
    /// </summary>
    [Fact(DisplayName = "TEST-4: the User role alone can read the list; anonymous gets 401")]
    public async Task Auth_UserRoleIsEnough_AnonymousIsChallenged()
    {
        var anonymous = await CreateUnauthenticatedClient().GetAsync($"{ItemRoute}?todoListId={_listId}", CancellationToken);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var userRole = await CreateUserRoleClient().GetAsync($"{ItemRoute}?todoListId={_listId}", CancellationToken);
        userRole.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Built inline rather than through <see cref="TodoListTestSeedHelper"/>: the point of these tests is that
    /// every projected column carries a distinguishable value, and the helper's signature covers about half of
    /// them. Widening it for one file's benefit would push the defaults onto every other caller.
    /// </summary>
    private async Task<(long ListId, long ItemId, long UncategorisedItemId, long PairedActivityId, Guid FirstStepId, Guid SecondStepId)>
        SeedProjectionListAsync()
    {
        await using var db = CreateDbContext();

        var role = new ActivityRole
        {
            UserId = UserId, Name = "projection-role", Text = "role note", Color = "#010203", Icon = "mdi-role"
        };
        var category = new ActivityCategory
        {
            UserId = UserId, Name = "projection-category", Text = "category note", Color = "#040506", Icon = "mdi-category"
        };
        db.Set<ActivityRole>().Add(role);
        db.Set<ActivityCategory>().Add(category);
        await db.SaveChangesAsync(CancellationToken);

        var activity = new Activity
        {
            UserId = UserId, Name = "projection-activity", Text = "activity note", IsUnavoidable = true,
            RoleId = role.Id, CategoryId = category.Id
        };
        var uncategorised = new Activity { UserId = UserId, Name = "uncategorised-activity", RoleId = role.Id };
        var paired = new Activity { UserId = UserId, Name = "paired-leisure-activity", RoleId = role.Id };
        db.Set<Activity>().AddRange(activity, uncategorised, paired);

        var priority = new TaskPriority { UserId = UserId, Text = "projection-priority", Color = "#abcdef", Priority = 392 };
        db.Set<TaskPriority>().Add(priority);
        await db.SaveChangesAsync(CancellationToken);

        var listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, UserId, "Projection list", ct: CancellationToken);

        var firstStep = new TodoListStep { Name = "step one", Order = 1, IsDone = true, Note = "step note" };
        var secondStep = new TodoListStep { Name = "step two", Order = 2 };

        var item = new TodoListItem
        {
            UserId = UserId,
            ActivityId = activity.Id,
            TaskPriorityId = priority.Id,
            TodoListId = listId,
            DoneCount = 2,
            TotalCount = 3,
            Note = "item note",
            DueDate = new DateOnly(2026, 3, 14),
            DueTime = new TimeOnly(15, 9),
            IsDone = true,
            DisplayOrder = 4200,
            SuggestedTime = new IntTime(1, 30),
            PairedLeisureActivityId = paired.Id,
            Steps = [firstStep, secondStep]
        };
        var uncategorisedItem = new TodoListItem
        {
            UserId = UserId,
            ActivityId = uncategorised.Id,
            TaskPriorityId = priority.Id,
            TodoListId = listId,
            DisplayOrder = 4300
        };
        db.Set<TodoListItem>().AddRange(item, uncategorisedItem);
        await db.SaveChangesAsync(CancellationToken);

        return (listId, item.Id, uncategorisedItem.Id, paired.Id, firstStep.Id, secondStep.Id);
    }

    private async Task<List<TodoListItemResponse>> ReadAsync(long todoListId)
        => JsonSerializer.Deserialize<List<TodoListItemResponse>>(await ReadRawAsync(todoListId), JsonOpts)!;

    /// <summary>
    /// The raw body, because <see cref="Projection_OmitsSuggestedTime"/> asserts on a property being
    /// <i>absent</i> — which deserialising into the response record would quietly hide.
    /// </summary>
    private async Task<string> ReadRawAsync(long todoListId)
    {
        var response = await CreateClient().GetAsync($"{ItemRoute}?todoListId={todoListId}", CancellationToken);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        return body;
    }
}
