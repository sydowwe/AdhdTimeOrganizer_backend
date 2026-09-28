using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
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
    private long _readNovelActivityId;

    protected override async Task SeedAsync(DbContext db)
    {
        var role = new ActivityRole { UserId = UserId, Name = "Recap Role", Color = "#223344" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        var writeReport = new Activity { UserId = UserId, Name = "Write the report", RoleId = role.Id };
        var tidyDesk = new Activity { UserId = UserId, Name = "Tidy the desk", RoleId = role.Id };
        var readNovel = new Activity { UserId = UserId, Name = "Read a novel", RoleId = role.Id };
        db.Set<Activity>().AddRange(writeReport, tidyDesk, readNovel);

        var todoList = new TodoList { UserId = UserId, Name = "Recap List" };
        db.Set<TodoList>().Add(todoList);

        var priority = new TaskPriority { UserId = UserId, Text = "Normal", Color = "#556677", Priority = 1 };
        db.Set<TaskPriority>().Add(priority);

        await db.SaveChangesAsync(CancellationToken);

        _writeReportActivityId = writeReport.Id;
        _tidyDeskActivityId = tidyDesk.Id;
        _readNovelActivityId = readNovel.Id;
        _todoListId = todoList.Id;
        _taskPriorityId = priority.Id;
    }

    private async Task<long> CreateItemAsync(HttpClient client, long activityId, long? pairedLeisureActivityId = null)
    {
        var response = await client.PostAsJsonAsync("/api/todo-list-item", new
        {
            activityId,
            taskPriorityId = _taskPriorityId,
            todoListId = _todoListId,
            pairedLeisureActivityId
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
    /// Day bounding is half-open, in the user's zone — UTC for the fixture's test user, so the edges
    /// here are UTC midnights. The two edge instants are what a closed range or an off-by-one would get
    /// wrong, and neither would fail loudly. The zone itself is pinned by
    /// <see cref="Recap_BoundsTheDayInTheUsersZone"/>.
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

    private async Task SetCompletedAsync(long id, DateTime completedUtc)
    {
        await using var db = CreateDbContext();
        var item = await db.Set<TodoListItem>().FirstAsync(i => i.Id == id, CancellationToken);
        item.CompletedTimestamp = completedUtc;
        await db.SaveChangesAsync(CancellationToken);
    }

    /// <summary>
    /// The earned-reward strip keys a waiting reward by item and offers the paired activity, so each row
    /// has to carry both, plus the completion instant it expires from. Asserted on the raw JSON as well:
    /// a field missing from the wire deserialises to a default indistinguishable from "no pairing".
    /// </summary>
    [Fact]
    public async Task Recap_CarriesTheItemId_ThePairing_AndTheCompletionInstant()
    {
        var client = CreateUserRoleClient();

        var pairedId = await CreateItemAsync(client, _writeReportActivityId, _readNovelActivityId);
        var plainId = await CreateItemAsync(client, _tidyDeskActivityId);
        await CompleteAsync(client, pairedId);
        await CompleteAsync(client, plainId);

        var recap = await RecapAsync(client, Today);

        var paired = recap.Items.Should().ContainSingle(i => i.ItemId == pairedId).Subject;
        paired.Name.Should().Be("Write the report");
        paired.PairedLeisureActivityId.Should().Be(_readNovelActivityId);
        paired.CompletedTimestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        paired.RewardTaken.Should().BeFalse("nothing has been logged on the leisure activity yet");

        var plain = recap.Items.Should().ContainSingle(i => i.ItemId == plainId).Subject;
        plain.PairedLeisureActivityId.Should().BeNull("an unpaired item is still in the recap; the client filters");
        plain.RewardTaken.Should().BeFalse();

        var raw = await client.GetStringAsync($"/api/todo-list-item/daily-recap?date={Today:yyyy-MM-dd}", CancellationToken);
        using var doc = JsonDocument.Parse(raw);
        var rows = doc.RootElement.GetProperty("items").EnumerateArray().ToList();

        var pairedRow = rows.Single(r => r.GetProperty("itemId").GetInt64() == pairedId);
        pairedRow.GetProperty("pairedLeisureActivityId").GetInt64().Should().Be(_readNovelActivityId);
        pairedRow.GetProperty("rewardTaken").GetBoolean().Should().BeFalse();
        pairedRow.GetProperty("completedTimestamp").GetString().Should().EndWith("Z",
            "the client reads the instant in the user's zone, so it has to arrive marked as UTC");

        var plainRow = rows.Single(r => r.GetProperty("itemId").GetInt64() == plainId);
        plainRow.GetProperty("pairedLeisureActivityId").ValueKind.Should().Be(JsonValueKind.Null,
            "the contract is an explicit null, not an absent field");
    }

    /// <summary>
    /// "Taken" is derived: a recording of the paired activity that starts at or after the completion.
    /// One that started before it was not the reward, however close — that is the case a plain "any
    /// recording today" check would get wrong.
    /// </summary>
    [Fact]
    public async Task RewardTaken_OnlyByARecordingOfThePairedActivity_StartingAfterCompletion()
    {
        var client = CreateUserRoleClient();
        var id = await CreateItemAsync(client, _writeReportActivityId, _readNovelActivityId);
        await CompleteAsync(client, id);

        // Pinned mid-day so the "after" recording cannot spill into tomorrow on a run near midnight.
        var completedAt = DateTime.UtcNow.Date.AddHours(10);
        await SetCompletedAsync(id, completedAt);

        await LogTimeAsync(_readNovelActivityId, null, completedAt.AddMinutes(-30), 600);
        // The item's own activity, after completion: time spent on the work is not the reward.
        await LogTimeAsync(_writeReportActivityId, null, completedAt.AddMinutes(30), 600);

        (await RecapAsync(client, Today)).Items.Should().ContainSingle()
            .Which.RewardTaken.Should().BeFalse("the only leisure recording started before the item was done");

        await LogTimeAsync(_readNovelActivityId, null, completedAt.AddHours(1), 1800);

        (await RecapAsync(client, Today)).Items.Should().ContainSingle()
            .Which.RewardTaken.Should().BeTrue();
    }

    /// <summary>
    /// Reopening un-earns: the interceptor clears the stamp, and a row with no stamp is not in the recap.
    /// </summary>
    [Fact]
    public async Task ReopenedItem_LeavesTheRecap()
    {
        var client = CreateUserRoleClient();
        var id = await CreateItemAsync(client, _writeReportActivityId, _readNovelActivityId);
        await CompleteAsync(client, id);

        (await RecapAsync(client, Today)).Items.Should().ContainSingle(i => i.ItemId == id);

        var undo = await client.PatchAsJsonAsync("/api/todo-list-item/toggle-is-done", new
        {
            ids = new[] { id },
            forceValue = false
        }, JsonOpts, CancellationToken);
        undo.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await RecapAsync(client, Today)).Items.Should().BeEmpty();
    }

    /// <summary>
    /// The day is the user's. In July Bratislava is UTC+2, so 00:30 local on the 15th is 22:30 UTC on
    /// the 14th: under a UTC day it lands in the wrong recap and its reward never waits. The logged time
    /// and the reward recording sit in the same early-morning window, so all three reads are pinned to
    /// the user's day, not just the item query. Fixed dates keep the offset exactly two hours.
    /// </summary>
    [Fact]
    public async Task Recap_BoundsTheDayInTheUsersZone()
    {
        await using (var db = CreateDbContext())
        {
            var user = await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == UserId, CancellationToken);
            user.Timezone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bratislava");
            await db.SaveChangesAsync(CancellationToken);
        }

        var client = CreateUserRoleClient();
        var id = await CreateItemAsync(client, _writeReportActivityId, _readNovelActivityId);
        await CompleteAsync(client, id);

        var completedAt = new DateTime(2026, 7, 14, 22, 30, 0, DateTimeKind.Utc);
        await SetCompletedAsync(id, completedAt);
        await LogTimeAsync(_writeReportActivityId, id, new DateTime(2026, 7, 14, 22, 5, 0, DateTimeKind.Utc), 1200);
        await LogTimeAsync(_readNovelActivityId, null, new DateTime(2026, 7, 14, 23, 0, 0, DateTimeKind.Utc), 1800);

        var local15th = await RecapAsync(client, new DateOnly(2026, 7, 15));
        var row = local15th.Items.Should().ContainSingle(
            "00:30 in Bratislava on the 15th is the 15th for the user, whatever UTC calls it").Subject;
        row.ItemId.Should().Be(id);
        row.TimeLoggedSeconds.Should().Be(1200, "00:05 local is inside the user's 15th");
        row.RewardTaken.Should().BeTrue("01:00 local is after completion and inside the user's 15th");

        (await RecapAsync(client, new DateOnly(2026, 7, 14))).Items.Should().BeEmpty(
            "the UTC date of the completion is not the user's date");
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
