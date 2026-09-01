using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// The three ids on <c>ActivityHistoryRequest</c> that the caller supplies and the server does not
/// mint: <c>ActivityId</c>, <c>TodoListItemId</c>, <c>RoutineTodoListId</c>.
/// </summary>
/// <remarks>
/// <para>
/// Nothing outside the endpoint can check them. The row's <c>UserId</c> is stamped server-side, so the
/// insert is a legitimate row of the caller's that points at somebody else's; the <c>Activity</c> FK
/// enforces existence, not ownership; and the two item columns carry no navigation at all. The failure
/// this pins is therefore invisible to every other layer, and its symptom is not an error but a
/// <b>poisoned read</b> — <c>Activity</c> is <c>IEntityWithUser</c>, so the global filter empties the
/// <c>Include</c> on every read path and <c>ActivityHistoryResponse.Projection</c> materializes a
/// non-nullable <c>Id</c> off the null.
/// </para>
/// <para>
/// The two item checks additionally exercise the <c>IUserOwnedReferenceVerifier</c> seam end to end:
/// History cannot see the slices that own those rows, so a foreign id is only refused if TodoLists' and
/// Routines' verifiers actually resolved under the keys Core declares. <c>SeamWiringTests</c> pins the
/// registration; these two pin the behaviour.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class ActivityHistoryReferencedIdOwnershipTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private const long UserId = FakeLoggedUserService.TestUserId;

    // ---- create ----------------------------------------------------------------------------------

    [Fact(DisplayName = "Creating a recording against another user's activity is rejected")]
    public async Task Create_WithForeignActivityId_IsRejected()
    {
        var foreignActivityId = await SeedForeignActivityAsync();

        var response = await PostAsync(NewBody(foreignActivityId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "an activity the caller does not own must be indistinguishable from one that does not exist");
        (await CountRowsAsync(foreignActivityId)).Should().Be(0,
            "the row must not exist at all -- created it would be the caller's own row pointing at a "
            + "stranger's activity, which empties the include on every subsequent read of that day");
    }

    [Fact(DisplayName = "Creating a recording linked to another user's to-do item is rejected")]
    public async Task Create_WithForeignTodoListItemId_IsRejected()
    {
        var ownActivityId = await SeedOwnActivityAsync("Own for foreign item");
        var foreignItemId = await SeedForeignTodoListItemAsync();

        var response = await PostAsync(NewBody(ownActivityId, todoListItemId: foreignItemId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "TodoListItemId crosses a slice boundary, so only the IUserOwnedReferenceVerifier seam can "
            + "refuse it -- a 201 here means TodoLists' verifier did not resolve under its key");
        (await CountRowsAsync(ownActivityId)).Should().Be(0);
    }

    [Fact(DisplayName = "Creating a recording linked to another user's routine item is rejected")]
    public async Task Create_WithForeignRoutineTodoListId_IsRejected()
    {
        var ownActivityId = await SeedOwnActivityAsync("Own for foreign routine item");
        var foreignRoutineItemId = await SeedForeignRoutineTodoListAsync();

        var response = await PostAsync(NewBody(ownActivityId, routineTodoListId: foreignRoutineItemId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "same seam, Routines' half of it");
        (await CountRowsAsync(ownActivityId)).Should().Be(0);
    }

    /// <summary>
    /// The control. Without it every rejection above would still pass with the endpoint hard-wired to
    /// 404, and the to-do link is the one the daily recap depends on.
    /// </summary>
    [Fact(DisplayName = "The caller's own activity and to-do item are accepted and linked")]
    public async Task Create_WithOwnIds_Succeeds()
    {
        var ownActivityId = await SeedOwnActivityAsync("Own accepted");
        var ownItemId = await SeedOwnTodoListItemAsync(ownActivityId);

        var response = await PostAsync(NewBody(ownActivityId, todoListItemId: ownItemId));

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = CreateDbContext();
        var row = await db.Set<ActivityHistory>()
            .SingleAsync(h => h.ActivityId == ownActivityId, CancellationToken);
        row.TodoListItemId.Should().Be(ownItemId, "the link is what makes the recap's per-item time exact");
    }

    /// <summary>
    /// Archival is not ownership. The check lifts <c>AppQueryFilters.ActivityArchived</c> on purpose —
    /// archiving hides an activity from the pickers, and turning it into a 404 would make a recording
    /// against it (or an edit of one made before the archiving) impossible to save.
    /// </summary>
    [Fact(DisplayName = "An archived activity of the caller's own is still accepted")]
    public async Task Create_WithOwnArchivedActivity_Succeeds()
    {
        var ownActivityId = await SeedOwnActivityAsync("Own archived", isArchived: true);

        var response = await PostAsync(NewBody(ownActivityId));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ---- update ----------------------------------------------------------------------------------

    /// <summary>
    /// The edit path can repoint an existing row just as easily as an insert can create one, and it is
    /// the likelier one to be forgotten: the row itself is already the caller's, so every other guard
    /// on the request passes.
    /// </summary>
    [Fact(DisplayName = "Repointing an existing recording at another user's activity is rejected")]
    public async Task Update_WithForeignActivityId_IsRejected()
    {
        var ownActivityId = await SeedOwnActivityAsync("Own before edit");
        var foreignActivityId = await SeedForeignActivityAsync();

        long rowId;
        await using (var seed = CreateDbContext())
        {
            var row = NewHistory(ownActivityId);
            seed.Set<ActivityHistory>().Add(row);
            await seed.SaveChangesAsync(CancellationToken);
            rowId = row.Id;
        }

        var response = await CreateUserRoleClient().PutAsJsonAsync(
            $"/api/activity-history/{rowId}",
            NewBody(foreignActivityId),
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var db = CreateDbContext();
        var unchanged = await db.Set<ActivityHistory>().SingleAsync(h => h.Id == rowId, CancellationToken);
        unchanged.ActivityId.Should().Be(ownActivityId, "the refused request must not have been applied");
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private Task<HttpResponseMessage> PostAsync(object body) =>
        CreateUserRoleClient().PostAsJsonAsync(
            "/api/activity-history", body, JsonOpts, TestContext.Current.CancellationToken);

    private static object NewBody(long activityId, long? todoListItemId = null, long? routineTodoListId = null) => new
    {
        activityId,
        startTimestamp = DateTime.UtcNow.AddHours(-1),
        length = new { hours = 0, minutes = 30 },
        todoListItemId,
        routineTodoListId
    };

    private static ActivityHistory NewHistory(long activityId)
    {
        var start = DateTime.UtcNow.AddHours(-2);
        return new ActivityHistory
        {
            UserId = UserId,
            ActivityId = activityId,
            StartTimestamp = start,
            EndTimestamp = start.AddMinutes(30),
            Length = new IntTime(0, 30)
        };
    }

    private async Task<int> CountRowsAsync(long activityId)
    {
        await using var db = CreateDbContext();
        return await db.Set<ActivityHistory>()
            .IgnoreQueryFilters()
            .CountAsync(h => h.ActivityId == activityId, CancellationToken);
    }

    private async Task<long> SeedOwnActivityAsync(string name, bool isArchived = false)
    {
        await using var db = CreateDbContext();
        return await SeedActivityAsync(db, UserId, name, isArchived);
    }

    private async Task<long> SeedForeignActivityAsync()
    {
        var otherUserId = await OtherUserAsync();
        await using var db = CreateDbContext();
        return await SeedActivityAsync(db, otherUserId, "Foreign activity");
    }

    private async Task<long> SeedOwnTodoListItemAsync(long activityId)
    {
        await using var db = CreateDbContext();
        return await SeedTodoListItemAsync(db, UserId, activityId);
    }

    private async Task<long> SeedForeignTodoListItemAsync()
    {
        var otherUserId = await OtherUserAsync();
        await using var db = CreateDbContext();
        var activityId = await SeedActivityAsync(db, otherUserId, "Foreign item activity");
        return await SeedTodoListItemAsync(db, otherUserId, activityId);
    }

    private async Task<long> SeedForeignRoutineTodoListAsync()
    {
        var otherUserId = await OtherUserAsync();
        await using var db = CreateDbContext();
        var activityId = await SeedActivityAsync(db, otherUserId, "Foreign routine activity");

        var period = new RoutineTimePeriod
        {
            UserId = otherUserId,
            Text = $"Foreign period {Guid.NewGuid():N}",
            Color = "#abcdef",
            LengthInDays = 1,
            ResetAnchorDay = 1,
            StreakThreshold = 1,
            StreakGraceDays = 0
        };
        db.Set<RoutineTimePeriod>().Add(period);
        await db.SaveChangesAsync(CancellationToken);

        var item = new RoutineTodoList { UserId = otherUserId, ActivityId = activityId, TimePeriodId = period.Id };
        db.Set<RoutineTodoList>().Add(item);
        await db.SaveChangesAsync(CancellationToken);
        return item.Id;
    }

    /// <summary>
    /// A real second account through <c>UserManager</c> — a bare insert would skip the per-user default
    /// seeders and leave a user the FKs accept but the app never produces.
    /// </summary>
    private Task<long> OtherUserAsync() =>
        ActivityMergeTestSupport.SecondUserAsync(Fixture, $"referenced-id-{Guid.NewGuid():N}@test.com");

    private async Task<long> SeedActivityAsync(DbContext db, long userId, string name, bool isArchived = false)
    {
        var role = new ActivityRole { UserId = userId, Name = $"{name} Role {Guid.NewGuid():N}", Color = "#112233" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        var activity = new Activity { UserId = userId, Name = name, RoleId = role.Id, IsArchived = isArchived };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);
        return activity.Id;
    }

    private async Task<long> SeedTodoListItemAsync(DbContext db, long userId, long activityId)
    {
        var priority = new TaskPriority
        {
            UserId = userId,
            Text = $"Priority {Guid.NewGuid():N}",
            Color = "#445566",
            Priority = 1
        };
        db.Set<TaskPriority>().Add(priority);
        await db.SaveChangesAsync(CancellationToken);

        var item = new TodoListItem { UserId = userId, ActivityId = activityId, TaskPriorityId = priority.Id };
        db.Set<TodoListItem>().Add(item);
        await db.SaveChangesAsync(CancellationToken);
        return item.Id;
    }
}
