using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// SEC-1 — every foreign key on <c>POST /api/todo-list-item</c> is a client-supplied <c>long</c>, and
/// nothing below the endpoint can reject a stranger's id: the validator sees a magnitude, the FK
/// constraint is satisfied by whoever owns the target row, and the row being written is stamped with the
/// caller's own <c>UserId</c>, so the insert is perfectly legal. The global query filter scopes
/// <b>reads</b>; this is a <b>write</b>, so it never runs.
///
/// <para>The failure is silent in both directions. Nothing throws at create time, and the damage only
/// shows up later on a read: <c>TodoListItemResponse.Projection</c> reaches through the <em>required</em>
/// <c>TaskPriority</c> navigation, which the filter excludes for a foreign rank, so the item the user
/// just created comes back mangled or does not come back at all. Every assertion here is therefore on
/// rows and on status codes — <c>TodoListCrudAuthMatrixTests</c> already proves you cannot <em>read</em>
/// another user's row, which is a different question from whether you can <em>point at</em> one.</para>
///
/// <para>404 rather than 403 on all of them, deliberately: a 403 would confirm the id exists and make
/// another account's sequential ids enumerable. A missing id and a foreign one are the same answer —
/// see <c>TodoListReferenceOwnershipExtensions</c> and <c>ActivityIdOwnershipExtensions</c>.</para>
///
/// <para>What is deliberately NOT asserted: that <c>PairedLeisureActivityId</c> names a leisure activity.
/// This slice cannot see ActivityProfiles, and restricting the picker is the frontend's job —
/// <c>TodoListItemPairingTests</c> pins that decision. Ownership is a separate question, and it <em>is</em>
/// checked.</para>
/// </summary>
[Collection("Postgres")]
public class CreateTodoListItemForeignKeyOwnershipTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    // task_priority is unique on (UserId, Priority) and the whole collection shares one database.
    // Rank band 340-349 is this file's; no other file in the suite uses it.
    private const int MyPriorityRank = 340;
    private const int TheirPriorityRank = 341;

    /// <summary>An id nobody owns, for the "does a non-existent id 404 rather than 500" scenarios.</summary>
    private const long NoSuchId = 999_999_999;

    private long _myListId;
    private long _myPriorityId;
    private long _myActivityId;
    private long _myLeisureActivityId;

    private long _theirListId;
    private long _theirPriorityId;
    private long _theirActivityId;

    protected override async Task SeedAsync(DbContext db)
    {
        _myListId = await TodoListTestSeedHelper.SeedTodoListAsync(db, UserId, "fk-ownership-mine", ct: CancellationToken);
        _myPriorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, MyPriorityRank, UserId, "fk-ownership-mine", CancellationToken);
        _myActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "fk-ownership-mine", UserId, CancellationToken);
        _myLeisureActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "fk-ownership-mine-leisure", UserId, CancellationToken);

        var otherUserId = await EnsureOtherUserIdAsync(db);
        _theirListId = await TodoListTestSeedHelper.SeedTodoListAsync(db, otherUserId, "fk-ownership-theirs", ct: CancellationToken);
        _theirPriorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, TheirPriorityRank, otherUserId, "fk-ownership-theirs", CancellationToken);
        _theirActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "fk-ownership-theirs", otherUserId, CancellationToken);
    }

    private async Task<long> EnsureOtherUserIdAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
        return ReminderSeedHelper.OtherUserId;
    }

    /// <summary>
    /// All four ids default to the caller's own rows; each scenario overrides exactly the one it is
    /// about, so a rejection can only be attributed to that id.
    /// </summary>
    private object CreateBody(
        long? activityId = null,
        long? taskPriorityId = null,
        long? todoListId = null,
        long? pairedLeisureActivityId = null) => new
    {
        activityId = activityId ?? _myActivityId,
        taskPriorityId = taskPriorityId ?? _myPriorityId,
        todoListId = todoListId ?? _myListId,
        pairedLeisureActivityId
    };

    private Task<HttpResponseMessage> PostAsync(object body) =>
        CreateUserRoleClient().PostAsJsonAsync("/api/todo-list-item", body, JsonOpts, CancellationToken);

    /// <summary>Counts through <c>IgnoreQueryFilters</c> — the whole point is that a bad row would be invisible to a filtered read.</summary>
    private async Task<int> CountItemsAsync()
    {
        await using var db = CreateDbContext();
        return await db.Set<TodoListItem>().IgnoreQueryFilters().CountAsync(CancellationToken);
    }

    // ---------------------------------------------------------------------------------------------
    // 1-2, 5: a foreign id on each of the four FKs is rejected, and nothing is written.
    // ---------------------------------------------------------------------------------------------

    [Fact(DisplayName = "SEC-1: a TodoListId belonging to another user is rejected, not written")]
    public async Task Create_WithForeignTodoListId_Is404_AndWritesNothing()
    {
        var response = await PostAsync(CreateBody(todoListId: _theirListId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a list the caller cannot see must be indistinguishable from one that does not exist");

        await using var db = CreateDbContext();
        (await db.Set<TodoListItem>().IgnoreQueryFilters()
                .AnyAsync(i => i.TodoListId == _theirListId, CancellationToken))
            .Should().BeFalse("the caller's task must not land on a stranger's list");
    }

    [Fact(DisplayName = "SEC-1: a TaskPriorityId belonging to another user is rejected, not written")]
    public async Task Create_WithForeignTaskPriorityId_Is404_AndWritesNothing()
    {
        var response = await PostAsync(CreateBody(taskPriorityId: _theirPriorityId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var db = CreateDbContext();
        (await db.Set<TodoListItem>().IgnoreQueryFilters()
                .AnyAsync(i => i.TaskPriorityId == _theirPriorityId, CancellationToken))
            .Should().BeFalse("a task ranked by a stranger's priority is unreadable to its own owner — see the read probes below");
    }

    /// <summary>
    /// The same hole through <c>IActivityIdRequest</c> rather than through this slice's own ids —
    /// <c>Activity</c> is <c>IEntityWithUser</c> too, and it is wired by <c>IsManyWithOneActivity()</c>.
    /// </summary>
    [Fact(DisplayName = "SEC-1: an ActivityId belonging to another user is rejected, not written")]
    public async Task Create_WithForeignActivityId_Is404_AndWritesNothing()
    {
        var response = await PostAsync(CreateBody(activityId: _theirActivityId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var db = CreateDbContext();
        (await db.Set<TodoListItem>().IgnoreQueryFilters()
                .AnyAsync(i => i.ActivityId == _theirActivityId, CancellationToken))
            .Should().BeFalse();
    }

    /// <summary>
    /// Ownership is a different question from leisure-ness. The pairing id stays unvalidated as to
    /// <em>what kind</em> of activity it names — that is the picker's job and this slice cannot answer it
    /// — but it must still belong to the caller.
    /// </summary>
    [Fact(DisplayName = "SEC-1: a PairedLeisureActivityId belonging to another user is rejected, not written")]
    public async Task Create_WithForeignPairedLeisureActivityId_Is404_AndWritesNothing()
    {
        var response = await PostAsync(CreateBody(pairedLeisureActivityId: _theirActivityId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await CountItemsAsync()).Should().Be(0);
    }

    // ---------------------------------------------------------------------------------------------
    // 3: the downstream damage a foreign TaskPriorityId causes.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// What turns SEC-1 from tidiness into a defect: "the task I just created does not appear on my
    /// list". The row is seeded directly rather than through the API because the create path now
    /// rejects it — this is the MIG-3 shape, a row that predates the guard, and it is the reason the
    /// guard has to exist rather than being left to the frontend.
    ///
    /// <para>The symptom is asserted as a disjunction on purpose: the projection reaches
    /// <c>e.TaskPriority.Text</c> through a required navigation the filter excludes, and whether that
    /// surfaces as a 500, a dropped row or a null-populated <c>TaskPriority</c> is an EF translation
    /// detail. Any of the three is the same user-visible bug, and pinning one of them would make this
    /// test fail on an EF upgrade for the wrong reason. What must never happen is the fourth outcome:
    /// the item coming back looking intact.</para>
    /// </summary>
    [Fact(DisplayName = "SEC-1: a task ranked by another user's TaskPriority does not appear on its owner's list")]
    public async Task ItemWithForeignTaskPriority_IsNotReadableByItsOwner()
    {
        long itemId;
        await using (var db = CreateDbContext())
            itemId = await TodoListTestSeedHelper.SeedTodoListItemAsync(
                db, _myActivityId, _theirPriorityId, _myListId, UserId, ct: CancellationToken);

        var client = CreateUserRoleClient();

        var listOutcome = await ProbeAsync(() => client.GetAsync($"/api/todo-list-item?todoListId={_myListId}", CancellationToken),
            async r =>
            {
                var items = await r.Content.ReadFromJsonAsync<List<TodoListItemResponse>>(JsonOpts, CancellationToken);
                return items?.SingleOrDefault(i => i.Id == itemId);
            });

        // Written to the test output on success as well as failure: which of the three symptoms the
        // broken join produces is the finding, and a passing assertion would otherwise swallow it.
        TestContext.Current.TestOutputHelper?.WriteLine($"SEC-1 symptom, GET /api/todo-list-item: {listOutcome}");

        listOutcome.IsIntact.Should().BeFalse(
            $"a task ranked by a rank the caller cannot see must not read back as a well-formed item; GET /api/todo-list-item answered {listOutcome}");

        // GetById is a different code path into the same projection — TodoListItemPairingTests
        // establishes that both reads get probed rather than assuming they agree.
        var byIdOutcome = await ProbeAsync(() => client.GetAsync($"/api/todo-list-item/{itemId}", CancellationToken),
            async r => await r.Content.ReadFromJsonAsync<TodoListItemResponse>(JsonOpts, CancellationToken));

        TestContext.Current.TestOutputHelper?.WriteLine($"SEC-1 symptom, GET /api/todo-list-item/{{id}}: {byIdOutcome}");

        byIdOutcome.IsIntact.Should().BeFalse(
            $"GET /api/todo-list-item/{{id}} answered {byIdOutcome}");
    }

    /// <summary>
    /// The observed outcome of one read, in the three shapes the broken join can take plus the one it
    /// must not take. <see cref="ProbeResult.IsIntact"/> is the only assertion; the rest is the message.
    /// </summary>
    private sealed record ProbeResult(HttpStatusCode? Status, string Detail, bool IsIntact)
    {
        public override string ToString() => Status is null ? Detail : $"{(int)Status} {Status} — {Detail}";
    }

    /// <summary>
    /// Runs one read and classifies it. A throw counts as damage: the host may surface a materialization
    /// failure as an unhandled exception through TestServer rather than as a 500, and both mean the same
    /// thing here.
    /// </summary>
    private static async Task<ProbeResult> ProbeAsync(
        Func<Task<HttpResponseMessage>> read, Func<HttpResponseMessage, Task<TodoListItemResponse?>> project)
    {
        HttpResponseMessage response;
        try
        {
            response = await read();
        }
        catch (Exception ex)
        {
            return new ProbeResult(null, $"threw {ex.GetType().Name}: {ex.Message}", false);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                return new ProbeResult(response.StatusCode, "the read failed outright", false);

            TodoListItemResponse? item;
            try
            {
                item = await project(response);
            }
            catch (Exception ex)
            {
                return new ProbeResult(response.StatusCode, $"the body did not deserialize: {ex.GetType().Name}", false);
            }

            if (item is null)
                return new ProbeResult(response.StatusCode, "the item was silently missing from the response", false);

            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            // TaskPriority is `required` on the DTO, but nothing enforces that at materialization time —
            // a filtered-out required navigation projects as null and lands here.
            if (item.TaskPriority is null || item.TaskPriority.Text is null)
                return new ProbeResult(response.StatusCode, "the item came back with a null-populated TaskPriority", false);

            return new ProbeResult(response.StatusCode, "the item came back intact, priority and all", true);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // 4: ids nobody owns behave the same way, and in particular do not reach Postgres.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A non-existent id must be answered by the same visibility check, not by an FK violation — every
    /// other write path in this API maps <c>DbUpdateException</c> through <c>DbUtils.HandleException</c>,
    /// and a 500 here would be both a worse answer and an existence oracle by omission.
    /// </summary>
    [Theory(DisplayName = "SEC-1: an id nobody owns 404s like a foreign one, and never 500s")]
    [InlineData("todoListId")]
    [InlineData("taskPriorityId")]
    [InlineData("activityId")]
    [InlineData("pairedLeisureActivityId")]
    public async Task Create_WithNonExistentId_Is404(string field)
    {
        var body = field switch
        {
            "todoListId" => CreateBody(todoListId: NoSuchId),
            "taskPriorityId" => CreateBody(taskPriorityId: NoSuchId),
            "activityId" => CreateBody(activityId: NoSuchId),
            "pairedLeisureActivityId" => CreateBody(pairedLeisureActivityId: NoSuchId),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
        };

        var response = await PostAsync(body);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "an unowned id must be rejected by the ownership check, not by a Postgres FK violation");

        (await CountItemsAsync()).Should().Be(0);
    }

    // ---------------------------------------------------------------------------------------------
    // 6: positive control. Without this, a guard that rejects everything passes all of the above.
    // ---------------------------------------------------------------------------------------------

    [Fact(DisplayName = "SEC-1 positive control: an item whose ids all belong to the caller is created and reads back intact")]
    public async Task Create_WithOwnedIds_Succeeds()
    {
        var response = await PostAsync(CreateBody(pairedLeisureActivityId: _myLeisureActivityId));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await response.Content.ReadFromJsonAsync<long>(JsonOpts, CancellationToken);

        await using var db = CreateDbContext();
        var item = await db.Set<TodoListItem>().IgnoreQueryFilters()
            .SingleAsync(i => i.Id == id, CancellationToken);

        item.UserId.Should().Be(UserId);
        item.TodoListId.Should().Be(_myListId);
        item.TaskPriorityId.Should().Be(_myPriorityId);
        item.ActivityId.Should().Be(_myActivityId);
        item.PairedLeisureActivityId.Should().Be(_myLeisureActivityId);

        var read = await CreateUserRoleClient().GetAsync($"/api/todo-list-item/{id}", CancellationToken);
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        var projected = await read.Content.ReadFromJsonAsync<TodoListItemResponse>(JsonOpts, CancellationToken);
        projected!.TaskPriority.Id.Should().Be(_myPriorityId);
    }
}
