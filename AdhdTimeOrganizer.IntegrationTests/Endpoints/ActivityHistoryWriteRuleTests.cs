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
/// TEST-6 -- the write rules on <c>POST/PUT api/activity-history</c> that are neither auth nor happy
/// path: <c>ActivityHistoryValidator</c>'s rules, the <c>(UserId, ActivityId, StartTimestamp)</c> unique
/// index, and <c>UpdateEntity</c>'s recomputation of <c>EndTimestamp</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ActivityHistoryCreateTests"/> (in <c>HistoryCrudAuthMatrixTests.cs</c>) owns the happy path
/// and the auth matrix; <see cref="ActivityHistoryReferencedIdOwnershipTests"/> owns the ownership of the
/// three client-supplied ids (SEC-4). Neither touches anything below.
/// </para>
/// <para>
/// The validator rules are asserted on the <b>error key</b>, not only the status: the mutual-exclusion
/// rule carries an explicit <c>.WithName(nameof(ActivityHistoryRequest.TodoListItemId))</c> so the SPA
/// can put the message beside a field, and a 400 whose key moved is a silent regression of that.
/// </para>
/// <para>
/// Not covered here, deliberately: a stale-token update. <c>ActivityHistoryRequest</c> does not implement
/// <c>IUpdateRequestWithRowVersion</c>, so no token is on the wire and <c>BaseUpdateEndpoint</c>'s header
/// documents the stale-form case as last-write-wins by design. There is nothing to assert that would not
/// be asserting the framework's default.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class ActivityHistoryWriteRuleTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private const long UserId = FakeLoggedUserService.TestUserId;

    // ---- 1. the mutual-exclusion rule --------------------------------------------------------------

    [Fact(DisplayName = "Neither item link set is accepted -- most recordings carry neither")]
    public async Task Create_WithNeitherItemLink_Succeeds()
    {
        var activityId = await SeedActivityAsync("Write rules neither");

        var response = await PostAsync(Body(activityId));

        await ShouldBeCreatedAsync(response);
    }

    [Fact(DisplayName = "Only TodoListItemId set is accepted")]
    public async Task Create_WithOnlyTodoListItemId_Succeeds()
    {
        var activityId = await SeedActivityAsync("Write rules todo only");
        var itemId = await SeedTodoListItemAsync(activityId);

        var response = await PostAsync(Body(activityId, todoListItemId: itemId));

        await ShouldBeCreatedAsync(response);
    }

    [Fact(DisplayName = "Only RoutineTodoListId set is accepted")]
    public async Task Create_WithOnlyRoutineTodoListId_Succeeds()
    {
        var activityId = await SeedActivityAsync("Write rules routine only");
        var routineItemId = await SeedRoutineTodoListAsync(activityId);

        var response = await PostAsync(Body(activityId, routineTodoListId: routineItemId));

        await ShouldBeCreatedAsync(response);
    }

    /// <summary>
    /// The rule itself: one recording is raised by completing one item, so both set would claim the same
    /// seconds for a to-do item and a routine item at once and double-count the row across two recaps.
    /// </summary>
    [Fact(DisplayName = "Both item links set is rejected, keyed on todoListItemId")]
    public async Task Create_WithBothItemLinks_IsRejectedOnTodoListItemId()
    {
        var activityId = await SeedActivityAsync("Write rules both links");
        var itemId = await SeedTodoListItemAsync(activityId);
        var routineItemId = await SeedRoutineTodoListAsync(activityId);

        var response = await PostAsync(Body(activityId, itemId, routineItemId));

        await ShouldBeValidationErrorForAsync(response, "todoListItemId");
        (await CountRowsAsync(activityId)).Should().Be(0, "a rejected request must not have inserted");
    }

    // ---- 2. length and id rules --------------------------------------------------------------------

    /// <summary>
    /// A zero-length recording is legitimate -- <c>time-of-day</c> credits its start hour with one entry
    /// and no seconds -- so the non-negative rule must be `>= 0`, not `> 0`.
    /// </summary>
    [Fact(DisplayName = "A zero-length recording is accepted and ends where it starts")]
    public async Task Create_WithZeroLength_Succeeds_AndEndEqualsStart()
    {
        var activityId = await SeedActivityAsync("Write rules zero length");

        var response = await PostAsync(Body(activityId, hours: 0, minutes: 0));

        await ShouldBeCreatedAsync(response);

        var row = await SingleRowAsync(activityId);
        row.Length.TotalSeconds.Should().Be(0);
        row.EndTimestamp.Should().Be(row.StartTimestamp,
            "EndTimestamp is a stored derivation of StartTimestamp + Length, and the attribution sink's "
            + "contiguous-window probe reads it");
    }

    [Fact(DisplayName = "A negative length is rejected")]
    public async Task Create_WithNegativeLength_IsRejected()
    {
        var activityId = await SeedActivityAsync("Write rules negative length");

        var response = await PostAsync(Body(activityId, minutes: -30));

        await ShouldBeValidationErrorForAsync(response, "length");
        (await CountRowsAsync(activityId)).Should().Be(0);
    }

    [Fact(DisplayName = "A null length is rejected by the validator, not by a null reference")]
    public async Task Create_WithNullLength_IsRejected()
    {
        var activityId = await SeedActivityAsync("Write rules null length");

        var response = await PostAsync(new
        {
            activityId,
            startTimestamp = Start(),
            length = (object?)null,
            todoListItemId = (long?)null,
            routineTodoListId = (long?)null
        });

        await ShouldBeValidationErrorForAsync(response, "length");
    }

    [Fact(DisplayName = "activityId = 0 is rejected (NotEmpty), before any ownership lookup")]
    public async Task Create_WithZeroActivityId_IsRejected() =>
        await ShouldBeValidationErrorForAsync(await PostAsync(Body(0)), "activityId");

    [Fact(DisplayName = "todoListItemId = 0 is rejected (GreaterThan(0))")]
    public async Task Create_WithZeroTodoListItemId_IsRejected()
    {
        var activityId = await SeedActivityAsync("Write rules zero item id");

        await ShouldBeValidationErrorForAsync(
            await PostAsync(Body(activityId, todoListItemId: 0)), "todoListItemId");
    }

    [Fact(DisplayName = "A negative routineTodoListId is rejected (GreaterThan(0))")]
    public async Task Create_WithNegativeRoutineTodoListId_IsRejected()
    {
        var activityId = await SeedActivityAsync("Write rules negative routine id");

        await ShouldBeValidationErrorForAsync(
            await PostAsync(Body(activityId, routineTodoListId: -1)), "routineTodoListId");
    }

    // ---- 3. the unique index -----------------------------------------------------------------------

    /// <summary>
    /// "One row per activity per start instant". A duplicate is a client mistake -- a double-submitted
    /// save-to-history prompt is the obvious producer -- so it must answer a 4xx the SPA can act on, not
    /// a 500. <c>DbUtils.HandleException</c> maps the unique violation to 409.
    /// </summary>
    [Fact(DisplayName = "Posting the same (activityId, startTimestamp) twice answers 409, not 500")]
    public async Task Create_Duplicate_AnswersConflict()
    {
        var activityId = await SeedActivityAsync("Write rules duplicate");
        var body = Body(activityId);

        await ShouldBeCreatedAsync(await PostAsync(body));

        var second = await PostAsync(body);

        var text = await second.Content.ReadAsStringAsync(CancellationToken);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict,
            $"the unique index is a client-visible rule, not an internal fault. Body: {text}");
        (await CountRowsAsync(activityId)).Should().Be(1, "the first row must survive the refused second");
    }

    // ---- 4. EndTimestamp after an update -----------------------------------------------------------

    /// <summary>
    /// <c>UpdateEntity</c> writes <c>ActivityId</c>, <c>StartTimestamp</c> and <c>Length</c>, and must
    /// recompute <c>EndTimestamp</c> from the last two. It used to leave the old value behind, which is
    /// the only way those three columns can disagree -- and
    /// <c>ActivityHistoryTimeAttributionSink</c>'s contiguous-window probe matches on
    /// <c>EndTimestamp == windowStart</c>, so a stale end silently changes which heartbeat window
    /// extends which row.
    /// </summary>
    [Fact(DisplayName = "Shortening a recording moves its EndTimestamp with it")]
    public async Task Update_Shortening_RecomputesEndTimestamp()
    {
        var activityId = await SeedActivityAsync("Write rules end timestamp");
        var start = Start();

        var created = await PostAsync(Body(activityId, hours: 1, minutes: 0, start: start));
        var rowId = await ShouldBeCreatedAsync(created);

        var response = await CreateUserRoleClient().PutAsJsonAsync(
            $"api/activity-history/{rowId}",
            Body(activityId, hours: 0, minutes: 10, start: start),
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync(CancellationToken));

        var row = await SingleRowAsync(activityId);
        row.Length.TotalSeconds.Should().Be(600);
        row.EndTimestamp.Should().Be(row.StartTimestamp.AddSeconds(row.Length.TotalSeconds),
            "the three columns are one derivation; an edit that shortens a recording must not leave the "
            + "old end an hour out");
    }

    // ---- helpers -----------------------------------------------------------------------------------

    private Task<HttpResponseMessage> PostAsync(object body) =>
        CreateUserRoleClient().PostAsJsonAsync(
            "api/activity-history", body, JsonOpts, TestContext.Current.CancellationToken);

    /// <summary>Whole seconds, so the µs-precision round trip through <c>timestamptz</c> cannot blur an equality.</summary>
    private static DateTime Start() =>
        new(DateTime.UtcNow.Date.Ticks + TimeSpan.TicksPerHour * 9, DateTimeKind.Utc);

    private static object Body(long activityId, long? todoListItemId = null, long? routineTodoListId = null,
        int hours = 0, int minutes = 30, DateTime? start = null) => new
    {
        activityId,
        startTimestamp = start ?? Start(),
        length = new { hours, minutes },
        todoListItemId,
        routineTodoListId
    };

    private async Task<long> ShouldBeCreatedAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.GetInt64();
    }

    /// <summary>
    /// Status plus the error key. The key is the observable difference between the validator refusing the
    /// request and the database refusing the row, and the SPA renders the message beside that field.
    /// </summary>
    private async Task ShouldBeValidationErrorForAsync(HttpResponseMessage response, string property)
    {
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);

        var root = JsonDocument.Parse(body).RootElement;
        root.TryGetProperty("errors", out var errors).Should().BeTrue(body);

        errors.EnumerateObject().Select(e => e.Name)
            .Should().Contain(name => string.Equals(name, property, StringComparison.OrdinalIgnoreCase),
                $"the failure must be keyed to {property}. Body: {body}");
    }

    private async Task<int> CountRowsAsync(long activityId)
    {
        await using var db = CreateDbContext();
        return await db.Set<ActivityHistory>()
            .IgnoreQueryFilters()
            .CountAsync(h => h.ActivityId == activityId, CancellationToken);
    }

    private async Task<ActivityHistory> SingleRowAsync(long activityId)
    {
        await using var db = CreateDbContext();
        return await db.Set<ActivityHistory>()
            .SingleAsync(h => h.ActivityId == activityId, CancellationToken);
    }

    private async Task<long> SeedActivityAsync(string name)
    {
        await using var db = CreateDbContext();
        var role = new ActivityRole { UserId = UserId, Name = $"{name} Role {Guid.NewGuid():N}", Color = "#112233" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        var activity = new Activity { UserId = UserId, Name = $"{name} {Guid.NewGuid():N}", RoleId = role.Id };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);
        return activity.Id;
    }

    private async Task<long> SeedTodoListItemAsync(long activityId)
    {
        await using var db = CreateDbContext();
        var priority = new TaskPriority
        {
            UserId = UserId,
            Text = $"Priority {Guid.NewGuid():N}",
            Color = "#445566",
            Priority = 1
        };
        db.Set<TaskPriority>().Add(priority);
        await db.SaveChangesAsync(CancellationToken);

        var item = new TodoListItem { UserId = UserId, ActivityId = activityId, TaskPriorityId = priority.Id };
        db.Set<TodoListItem>().Add(item);
        await db.SaveChangesAsync(CancellationToken);
        return item.Id;
    }

    private async Task<long> SeedRoutineTodoListAsync(long activityId)
    {
        await using var db = CreateDbContext();
        var period = new RoutineTimePeriod
        {
            UserId = UserId,
            Text = $"Period {Guid.NewGuid():N}",
            Color = "#abcdef",
            LengthInDays = 1,
            ResetAnchorDay = 1,
            StreakThreshold = 1,
            StreakGraceDays = 0
        };
        db.Set<RoutineTimePeriod>().Add(period);
        await db.SaveChangesAsync(CancellationToken);

        var item = new RoutineTodoList { UserId = UserId, ActivityId = activityId, TimePeriodId = period.Id };
        db.Set<RoutineTodoList>().Add(item);
        await db.SaveChangesAsync(CancellationToken);
        return item.Id;
    }
}
