using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// `RoutineTodoListPeriodRules` — the `TimePeriodId` half of the two routine-item write validators,
/// through both of the endpoints that write it. Ownership first, then the suggestion buckets the
/// period's length decides, since create and update now run one shared implementation and nothing else
/// pins it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ownership.</b> Sibling of <see cref="ActivityIdOwnershipTests"/> — same hole, different id. The
/// row lands with the caller's own `UserId` and a stranger's `TimePeriodId`, so the attacker never sees
/// it again and nothing errors. It is a write into the *victim's* data: the reset job runs
/// unauthenticated with the global filter inert, so it pulls the foreign item into the victim's period
/// and divides by it, breaking a streak whose cause the victim cannot see and writing permanently wrong
/// `RoutinePeriodCompletion` rows.
/// </para>
/// <para>
/// The guard is a validator rule, so this is a 400 with a `TimePeriodId` failure rather than the 404
/// the `ActivityId` endpoint guard answers with. Either way a foreign id and a non-existent one are
/// refused identically, or the endpoint is an existence oracle over another account's ids.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class RoutineTodoListPeriodRulesTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // ---- ownership -------------------------------------------------------------------------------

    [Fact(DisplayName = "Creating a routine item in another user's time period is rejected")]
    public async Task CreateRoutineTodoList_WithForeignTimePeriodId_IsRejected()
    {
        var foreignPeriodId = await SeedForeignPeriodAsync();
        long activityId;
        await using (var db = CreateDbContext())
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "Own activity, foreign period", ct: CancellationToken);

        var response = await CreateUserRoleClient().PostAsJsonAsync(
            "/api/routine-todo-list",
            new { activityId, timePeriodId = foreignPeriodId },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a time period the caller does not own must read as one that does not exist");
        (await CountInPeriodAsync(foreignPeriodId)).Should().Be(0);
    }

    [Fact(DisplayName = "Moving a routine item into another user's time period is rejected")]
    public async Task UpdateRoutineTodoList_WithForeignTimePeriodId_IsRejected()
    {
        var foreignPeriodId = await SeedForeignPeriodAsync();
        long activityId, ownPeriodId, itemId;
        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "Own routine before move", ct: CancellationToken);
            ownPeriodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, ct: CancellationToken);
            itemId = await TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, activityId, ownPeriodId, ct: CancellationToken);
        }

        var response = await CreateUserRoleClient().PutAsJsonAsync(
            $"/api/routine-todo-list/{itemId}",
            new
            {
                activityId,
                timePeriodId = foreignPeriodId,
                displayOrder = 1000L,
                isDone = false
            },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var check = CreateDbContext();
        (await check.Set<RoutineTodoList>().SingleAsync(r => r.Id == itemId, CancellationToken))
            .TimePeriodId.Should().Be(ownPeriodId, "the refused edit must not have been applied");
    }

    /// <summary>
    /// The control for the two rejections — without it the endpoints could be refusing every write.
    /// </summary>
    [Fact(DisplayName = "Creating a routine item in the caller's own time period still succeeds")]
    public async Task CreateRoutineTodoList_WithOwnTimePeriodId_Succeeds()
    {
        long activityId, periodId;
        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "Own activity, own period", ct: CancellationToken);
            periodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, ct: CancellationToken);
        }

        var response = await CreateUserRoleClient().PostAsJsonAsync(
            "/api/routine-todo-list",
            new { activityId, timePeriodId = periodId },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ---- suggestion buckets ----------------------------------------------------------------------

    [Fact(DisplayName = "Create: SuggestedDayOfMonth is refused on a weekly period")]
    public async Task CreateRoutineTodoList_WithDayOfMonthOnWeeklyPeriod_IsRejected()
    {
        long activityId, weeklyPeriodId;
        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "Weekly bucket rule", ct: CancellationToken);
            weeklyPeriodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, lengthInDays: 7, ct: CancellationToken);
        }

        var response = await CreateUserRoleClient().PostAsJsonAsync(
            "/api/routine-todo-list",
            new { activityId, timePeriodId = weeklyPeriodId, suggestedDayOfMonth = 15 },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CountInPeriodAsync(weeklyPeriodId)).Should().Be(0);
    }

    [Fact(DisplayName = "Update: SuggestedDays is refused on a monthly period")]
    public async Task UpdateRoutineTodoList_WithSuggestedDaysOnMonthlyPeriod_IsRejected()
    {
        long activityId, monthlyPeriodId, itemId;
        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "Monthly bucket rule", ct: CancellationToken);
            monthlyPeriodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, lengthInDays: 30, ct: CancellationToken);
            itemId = await TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, activityId, monthlyPeriodId, ct: CancellationToken);
        }

        var response = await CreateUserRoleClient().PutAsJsonAsync(
            $"/api/routine-todo-list/{itemId}",
            new
            {
                activityId,
                timePeriodId = monthlyPeriodId,
                displayOrder = 1000L,
                isDone = false,
                suggestedDays = new[] { DayOfWeek.Monday }
            },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private async Task<int> CountInPeriodAsync(long timePeriodId)
    {
        await using var db = CreateDbContext();
        return await db.Set<RoutineTodoList>()
            .IgnoreQueryFilters()
            .CountAsync(r => r.TimePeriodId == timePeriodId, CancellationToken);
    }

    private async Task<long> SeedForeignPeriodAsync()
    {
        var otherUserId = await ActivityMergeTestSupport.SecondUserAsync(
            Fixture, $"period-id-ownership-{Guid.NewGuid():N}@test.com");

        await using var db = CreateDbContext();
        return await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(
            db, otherUserId, "Foreign period", ct: CancellationToken);
    }
}
