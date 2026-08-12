using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Sydowwe.Framework.domain.valueObject;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// The daily recap — <c>GET /todo-list-item/daily-recap</c> — and the two mechanisms underneath it.
/// Everything here asserts on rows and payloads rather than on routes or metadata, because all three
/// moving parts fail <b>silently</b>:
/// <list type="bullet">
/// <item><c>TodoListItemCompletionInterceptor</c> is registered by hand in <c>Program.cs</c>. Drop that
/// registration and items still complete, still read back as done, and simply never appear in a recap
/// — no error anywhere.</item>
/// <item><c>ITodoListItemLoggedTimeSource</c> crosses a slice boundary. It resolves as a single service
/// so a missing registration throws, but a correct registration reading the <em>wrong column</em>
/// (falling back to activity matching) would double-count instead of failing.</item>
/// <item>The FK is <c>SetNull</c> and declared host-side, where deleting it is not a build error.</item>
/// </list>
/// </summary>
[Collection("Postgres")]
public class TodoListItemDailyRecapTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private long _todoListId;
    private long _taskPriorityId;
    private long _writeReportActivityId;
    private long _tidyDeskActivityId;

    protected override async Task SeedAsync(DbContext db)
    {
        var role = new ActivityRole { UserId = UserId, Name = "Recap Role", Color = "#223344" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        var writeReport = new Activity { UserId = UserId, Name = "Write the report", RoleId = role.Id };
        var tidyDesk = new Activity { UserId = UserId, Name = "Tidy the desk", RoleId = role.Id };
        db.Set<Activity>().AddRange(writeReport, tidyDesk);

        var todoList = new TodoList { UserId = UserId, Name = "Recap List" };
        db.Set<TodoList>().Add(todoList);

        var priority = new TaskPriority { UserId = UserId, Text = "Normal", Color = "#556677", Priority = 1 };
        db.Set<TaskPriority>().Add(priority);

        await db.SaveChangesAsync(CancellationToken);

        _writeReportActivityId = writeReport.Id;
        _tidyDeskActivityId = tidyDesk.Id;
        _todoListId = todoList.Id;
        _taskPriorityId = priority.Id;
    }

    private async Task<long> CreateItemAsync(HttpClient client, long activityId)
    {
        var response = await client.PostAsJsonAsync("/api/todo-list-item", new
        {
            activityId,
            taskPriorityId = _taskPriorityId,
            todoListId = _todoListId
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<long>(JsonOpts, CancellationToken);
    }

    private async Task CompleteAsync(HttpClient client, long id)
    {
        var response = await client.PatchAsJsonAsync("/api/todo-list-item/toggle-is-done", new
        {
            ids = new[] { id },
            forceValue = true
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private async Task<TodoListItemDailyRecapResponse> RecapAsync(HttpClient client, DateOnly date)
    {
        var response = await client.GetAsync($"/api/todo-list-item/daily-recap?date={date:yyyy-MM-dd}", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<TodoListItemDailyRecapResponse>(JsonOpts, CancellationToken))!;
    }

    /// <summary>
    /// Records time against an item the way the save-to-history prompt does, at a chosen instant so a
    /// test can place it on a day other than today.
    /// </summary>
    private async Task LogTimeAsync(long activityId, long? todoListItemId, DateTime startUtc, int seconds)
    {
        await using var db = CreateDbContext();
        db.Set<ActivityHistory>().Add(new ActivityHistory
        {
            UserId = UserId,
            ActivityId = activityId,
            StartTimestamp = startUtc,
            EndTimestamp = startUtc.AddSeconds(seconds),
            Length = new IntTime(seconds),
            TodoListItemId = todoListItemId
        });
        await db.SaveChangesAsync(CancellationToken);
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>
    /// The interceptor is the whole reason "completed today" is answerable. Asserted on the column
    /// rather than through the recap so a failure points at the stamp rather than at the join.
    /// </summary>
    [Fact]
    public async Task Completing_StampsTheTimestamp_AndUncompleting_ClearsIt()
    {
        var client = CreateUserRoleClient();
        var id = await CreateItemAsync(client, _writeReportActivityId);

        await using (var db = CreateDbContext())
        {
            var fresh = await db.Set<TodoListItem>().FirstAsync(i => i.Id == id, CancellationToken);
            fresh.CompletedTimestamp.Should().BeNull("an item is not complete when it is created");
        }

        await CompleteAsync(client, id);

        await using (var db = CreateDbContext())
        {
            var done = await db.Set<TodoListItem>().FirstAsync(i => i.Id == id, CancellationToken);
            done.IsDone.Should().BeTrue();
            done.CompletedTimestamp.Should().NotBeNull()
                .And.BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        }

        var undo = await client.PatchAsJsonAsync("/api/todo-list-item/toggle-is-done", new
        {
            ids = new[] { id },
            forceValue = false
        }, JsonOpts, CancellationToken);
        undo.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var db = CreateDbContext())
        {
            var reopened = await db.Set<TodoListItem>().FirstAsync(i => i.Id == id, CancellationToken);
            reopened.IsDone.Should().BeFalse();
            reopened.CompletedTimestamp.Should().BeNull("reopening a task un-completes it");
        }
    }

    /// <summary>
    /// Editing an already-done item must not walk its completion date forward — that would quietly
    /// drag every finished task into whichever day it was last touched.
    /// </summary>
    [Fact]
    public async Task EditingAnAlreadyDoneItem_DoesNotRestampIt()
    {
        var client = CreateUserRoleClient();
        var id = await CreateItemAsync(client, _writeReportActivityId);
        await CompleteAsync(client, id);

        DateTime stampedAt;
        await using (var db = CreateDbContext())
            stampedAt = (await db.Set<TodoListItem>().FirstAsync(i => i.Id == id, CancellationToken)).CompletedTimestamp!.Value;

        // Backdate it so a re-stamp would be unmistakable rather than a sub-second difference.
        await using (var db = CreateDbContext())
        {
            var item = await db.Set<TodoListItem>().FirstAsync(i => i.Id == id, CancellationToken);
            item.CompletedTimestamp = stampedAt.AddDays(-3);
            await db.SaveChangesAsync(CancellationToken);
        }

        var edit = await client.PutAsJsonAsync($"/api/todo-list-item/{id}", new
        {
            activityId = _writeReportActivityId,
            taskPriorityId = _taskPriorityId,
            isDone = true,
            displayOrder = 0,
            note = "touched"
        }, JsonOpts, CancellationToken);
        edit.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var assertDb = CreateDbContext();
        var after = await assertDb.Set<TodoListItem>().FirstAsync(i => i.Id == id, CancellationToken);
        after.CompletedTimestamp.Should().BeCloseTo(stampedAt.AddDays(-3), TimeSpan.FromSeconds(1),
            "an edit that does not change IsDone must leave the completion date alone");
    }

    [Fact]
    public async Task ZeroDay_ReturnsAnEmptyArray_NotNull()
    {
        var recap = await RecapAsync(CreateUserRoleClient(), Today.AddDays(-30));

        recap.Items.Should().NotBeNull().And.BeEmpty();
        recap.TotalTimeLoggedSeconds.Should().Be(0);
    }

    /// <summary>
    /// The main path: two items completed today, time recorded against each through the item link, and
    /// a header that reconciles with the rows.
    /// </summary>
    [Fact]
    public async Task Recap_ListsItemsCompletedThatDay_WithTheirLoggedTime()
    {
        var client = CreateUserRoleClient();

        var reportId = await CreateItemAsync(client, _writeReportActivityId);
        var deskId = await CreateItemAsync(client, _tidyDeskActivityId);

        await CompleteAsync(client, reportId);
        await CompleteAsync(client, deskId);

        var now = DateTime.UtcNow;
        await LogTimeAsync(_writeReportActivityId, reportId, now.Date.AddHours(9), 3600);
        await LogTimeAsync(_tidyDeskActivityId, deskId, now.Date.AddHours(11), 900);

        var recap = await RecapAsync(client, Today);

        recap.Items.Should().HaveCount(2);
        recap.Items.Should().ContainSingle(i => i.Name == "Write the report")
            .Which.TimeLoggedSeconds.Should().Be(3600);
        recap.Items.Should().ContainSingle(i => i.Name == "Tidy the desk")
            .Which.TimeLoggedSeconds.Should().Be(900);
        recap.TotalTimeLoggedSeconds.Should().Be(4500);
    }

    /// <summary>
    /// Declining the save-to-history prompt is an ordinary outcome, not a gap — the item still belongs
    /// in the recap, at zero.
    /// </summary>
    [Fact]
    public async Task ItemCompletedWithoutARecording_AppearsWithZeroSeconds()
    {
        var client = CreateUserRoleClient();
        var id = await CreateItemAsync(client, _writeReportActivityId);
        await CompleteAsync(client, id);

        var recap = await RecapAsync(client, Today);

        recap.Items.Should().ContainSingle().Which.TimeLoggedSeconds.Should().Be(0);
        recap.TotalTimeLoggedSeconds.Should().Be(0);
    }

    /// <summary>
    /// The reason the item link exists at all. Two items on the same activity, one completed and one
    /// not, with time recorded against only the completed one: activity-matching would credit both
    /// rows and report double the time that was actually spent.
    /// </summary>
    [Fact]
    public async Task TimeIsAttributedByItem_NotByActivity()
    {
        var client = CreateUserRoleClient();

        var completedId = await CreateItemAsync(client, _writeReportActivityId);
        await CompleteAsync(client, completedId);

        await LogTimeAsync(_writeReportActivityId, completedId, DateTime.UtcNow.Date.AddHours(9), 1800);

        // Same activity, recorded on the same day, but never linked to an item — the shape every
        // heartbeat-attributed row has. It must not leak into the completed item's total.
        await LogTimeAsync(_writeReportActivityId, null, DateTime.UtcNow.Date.AddHours(14), 7200);

        var recap = await RecapAsync(client, Today);

        recap.Items.Should().ContainSingle().Which.TimeLoggedSeconds.Should().Be(1800);
        recap.TotalTimeLoggedSeconds.Should().Be(1800);
    }

    /// <summary>
    /// Day bounding is half-open UTC. The two edge instants are what a closed range or an off-by-one
    /// would get wrong, and neither would fail loudly.
    /// </summary>
    [Fact]
    public async Task Recap_IsScopedToItsOwnDay()
    {
        var client = CreateUserRoleClient();

        var yesterdayId = await CreateItemAsync(client, _writeReportActivityId);
        var todayId = await CreateItemAsync(client, _tidyDeskActivityId);

        await CompleteAsync(client, yesterdayId);
        await CompleteAsync(client, todayId);

        var todayStart = DateTime.UtcNow.Date;

        await using (var db = CreateDbContext())
        {
            // The last tick of yesterday: included in yesterday, excluded from today.
            var item = await db.Set<TodoListItem>().FirstAsync(i => i.Id == yesterdayId, CancellationToken);
            item.CompletedTimestamp = todayStart.AddTicks(-1);
            await db.SaveChangesAsync(CancellationToken);
        }

        await using (var db = CreateDbContext())
        {
            // Midnight exactly: the first instant of today, not the last of yesterday.
            var item = await db.Set<TodoListItem>().FirstAsync(i => i.Id == todayId, CancellationToken);
            item.CompletedTimestamp = todayStart;
            await db.SaveChangesAsync(CancellationToken);
        }

        (await RecapAsync(client, Today)).Items
            .Should().ContainSingle().Which.Name.Should().Be("Tidy the desk");

        (await RecapAsync(client, Today.AddDays(-1))).Items
            .Should().ContainSingle().Which.Name.Should().Be("Write the report");
    }

    /// <summary>
    /// Recorded time is the source of truth and outlives the task it was recorded from. SetNull, not
    /// Cascade: a Cascade here would delete the user's history along with a tidied-up to-do list.
    /// </summary>
    [Fact]
    public async Task DeletingTheItem_KeepsTheRecording_AndUnlinksIt()
    {
        var client = CreateUserRoleClient();
        var id = await CreateItemAsync(client, _writeReportActivityId);
        await CompleteAsync(client, id);
        await LogTimeAsync(_writeReportActivityId, id, DateTime.UtcNow.Date.AddHours(9), 1200);

        var delete = await client.DeleteAsync($"/api/todo-list-item/{id}", CancellationToken);
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = CreateDbContext();
        var recordings = await db.Set<ActivityHistory>()
            .Where(h => h.ActivityId == _writeReportActivityId)
            .ToListAsync(CancellationToken);

        recordings.Should().ContainSingle("deleting a task must not delete the time recorded against it")
            .Which.TodoListItemId.Should().BeNull();
    }

    /// <summary>
    /// The recap reads rows belonging to one user. The global query filter covers this, but the
    /// endpoint also filters explicitly and this is what proves the two agree.
    /// </summary>
    [Fact]
    public async Task Recap_DoesNotLeakAnotherUsersCompletions()
    {
        var client = CreateUserRoleClient();
        var id = await CreateItemAsync(client, _writeReportActivityId);
        await CompleteAsync(client, id);

        await using var otherFactory = CreateFactory(["User"], UserId + 1);
        var otherClient = otherFactory.CreateClient();

        var response = await otherClient.GetAsync($"/api/todo-list-item/daily-recap?date={Today:yyyy-MM-dd}", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var recap = await response.Content.ReadFromJsonAsync<TodoListItemDailyRecapResponse>(JsonOpts, CancellationToken);

        recap!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Recap_RequiresAuthentication()
    {
        var response = await CreateUnauthenticatedClient()
            .GetAsync($"/api/todo-list-item/daily-recap?date={Today:yyyy-MM-dd}", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Recap_RejectsAMissingDate()
    {
        var response = await CreateUserRoleClient()
            .GetAsync("/api/todo-list-item/daily-recap", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
