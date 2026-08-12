using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// Temptation bundling — <c>TodoListItem.PairedLeisureActivityId</c>. Everything here is behavioural
/// rather than metadata-shaped, because all three properties under test fail *silently*:
/// <list type="bullet">
/// <item>a field dropped from the projection or the request mapping just reads back as null, which is
/// indistinguishable from "not paired" — the frontend hydrates it with <c>?? null</c>;</item>
/// <item>the FK's SetNull means deleting the paired leisure activity unpairs the task with no error,
/// so the only way to catch a Cascade slipping in is to assert the task still exists;</item>
/// <item>the self-pairing rule is a validator, and validators are attached by hand in
/// <c>Configure()</c> — an unattached one is not a build error.</item>
/// </list>
/// Note what is deliberately NOT asserted: that the paired activity has a backlog profile. This slice
/// cannot see AdhdTimeOrganizer.ActivityProfiles, and pairing is intentionally unvalidated beyond the
/// FK — restricting the choice to leisure activities is the picker's job.
/// </summary>
[Collection("Postgres")]
public class TodoListItemPairingTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private long _todoListId;
    private long _taskPriorityId;
    private long _workActivityId;
    private long _leisureActivityId;

    protected override async Task SeedAsync(DbContext db)
    {
        var role = new ActivityRole { UserId = UserId, Name = "Pairing Role", Color = "#112233" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        var work = new Activity { UserId = UserId, Name = "Do the taxes", RoleId = role.Id };
        var leisure = new Activity { UserId = UserId, Name = "Listen to a podcast", RoleId = role.Id };
        db.Set<Activity>().AddRange(work, leisure);

        var todoList = new TodoList { UserId = UserId, Name = "Pairing List" };
        db.Set<TodoList>().Add(todoList);

        var priority = new TaskPriority { UserId = UserId, Text = "Normal", Color = "#445566", Priority = 1 };
        db.Set<TaskPriority>().Add(priority);

        await db.SaveChangesAsync(CancellationToken);

        _workActivityId = work.Id;
        _leisureActivityId = leisure.Id;
        _todoListId = todoList.Id;
        _taskPriorityId = priority.Id;
    }

    private object CreateBody(long? pairedLeisureActivityId, long? activityId = null) => new
    {
        activityId = activityId ?? _workActivityId,
        taskPriorityId = _taskPriorityId,
        todoListId = _todoListId,
        pairedLeisureActivityId
    };

    private async Task<long> CreateItemAsync(HttpClient client, long? pairedLeisureActivityId)
    {
        var response = await client.PostAsJsonAsync("/api/todo-list-item", CreateBody(pairedLeisureActivityId), JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<long>(JsonOpts, CancellationToken);
    }

    private async Task<TodoListItemResponse> ReadItemAsync(HttpClient client, long id)
    {
        var response = await client.GetAsync($"/api/todo-list-item/{id}", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<TodoListItemResponse>(JsonOpts, CancellationToken))!;
    }

    /// <summary>
    /// The whole contract in one pass: the id survives create, the single read and the list read.
    /// The list route is asserted separately from GetById because they use different code paths into
    /// the same projection, and the list is what the to-do view actually renders from.
    /// </summary>
    [Fact]
    public async Task Create_RoundTripsThePairedActivityId_ThroughBothReads()
    {
        var client = CreateUserRoleClient();

        var id = await CreateItemAsync(client, _leisureActivityId);

        (await ReadItemAsync(client, id)).PairedLeisureActivityId.Should().Be(_leisureActivityId);

        var listResponse = await client.GetAsync($"/api/todo-list-item?todoListId={_todoListId}", CancellationToken);
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await listResponse.Content.ReadFromJsonAsync<List<TodoListItemResponse>>(JsonOpts, CancellationToken);

        items.Should().ContainSingle(i => i.Id == id)
            .Which.PairedLeisureActivityId.Should().Be(_leisureActivityId);
    }

    [Fact]
    public async Task Create_WithoutAPairing_ReadsBackAsNull()
    {
        var client = CreateUserRoleClient();

        var id = await CreateItemAsync(client, null);

        (await ReadItemAsync(client, id)).PairedLeisureActivityId.Should().BeNull();
    }

    /// <summary>
    /// PUT round-trips the whole item, so the frontend sends the current value back on every edit —
    /// this pins that the update actually writes what it was handed, in both directions. The clearing
    /// half matters as much as the preserving half: unpairing is done by sending null.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Update_WritesThePairingItWasGiven(bool startPaired)
    {
        var client = CreateUserRoleClient();
        var id = await CreateItemAsync(client, startPaired ? _leisureActivityId : null);

        long? incoming = startPaired ? null : _leisureActivityId;
        var response = await client.PutAsJsonAsync($"/api/todo-list-item/{id}", new
        {
            activityId = _workActivityId,
            taskPriorityId = _taskPriorityId,
            isDone = false,
            displayOrder = 0,
            pairedLeisureActivityId = incoming
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadItemAsync(client, id)).PairedLeisureActivityId.Should().Be(incoming);
    }

    /// <summary>
    /// SetNull, not Cascade. If the FK is ever regenerated as Cascade — which is what EF would pick if
    /// the relationship were declared as required — deleting a leisure activity would silently delete
    /// every task bundled with it. The surviving row is the assertion; the null is secondary.
    /// </summary>
    [Fact]
    public async Task DeletingThePairedLeisureActivity_UnpairsTheTask_ButKeepsIt()
    {
        var id = await CreateItemAsync(CreateUserRoleClient(), _leisureActivityId);

        await using (var db = CreateDbContext())
        {
            var leisure = await db.Set<Activity>().FirstAsync(a => a.Id == _leisureActivityId, CancellationToken);
            db.Remove(leisure);
            await db.SaveChangesAsync(CancellationToken);
        }

        await using var assertDb = CreateDbContext();
        var item = await assertDb.Set<TodoListItem>().FirstOrDefaultAsync(i => i.Id == id, CancellationToken);

        item.Should().NotBeNull("deleting the paired leisure activity must not delete the task bundled with it");
        item!.PairedLeisureActivityId.Should().BeNull();
    }

    [Fact]
    public async Task Create_RejectsPairingATaskWithItsOwnActivity()
    {
        var client = CreateUserRoleClient();

        var response = await client.PostAsJsonAsync("/api/todo-list-item", CreateBody(_workActivityId), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_RejectsPairingATaskWithItsOwnActivity()
    {
        var client = CreateUserRoleClient();
        var id = await CreateItemAsync(client, null);

        var response = await client.PutAsJsonAsync($"/api/todo-list-item/{id}", new
        {
            activityId = _workActivityId,
            taskPriorityId = _taskPriorityId,
            isDone = false,
            displayOrder = 0,
            pairedLeisureActivityId = _workActivityId
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
