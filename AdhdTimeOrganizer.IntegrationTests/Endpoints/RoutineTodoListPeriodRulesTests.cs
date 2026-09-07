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

    /// <summary>
    /// The other half of "a foreign id and a non-existent one are refused identically". The FK on
    /// `TimePeriodId` would refuse this on its own, but a database round trip is not the same guard: it
    /// answers 409 rather than 400, and it only fires for ids that exist nowhere — an id belonging to
    /// another account satisfies the FK perfectly. Both have to be the validator's 400, or the difference
    /// between the two status codes is an existence oracle over another account's id range.
    /// </summary>
    [Fact(DisplayName = "Creating a routine item in a time period that does not exist is rejected the same way")]
    public async Task CreateRoutineTodoList_WithNonexistentTimePeriodId_IsRejected()
    {
        const long nonexistentPeriodId = 999_999_999L;

        long activityId;
        await using (var db = CreateDbContext())
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "Own activity, no period", ct: CancellationToken);

        var response = await CreateUserRoleClient().PostAsJsonAsync(
            "/api/routine-todo-list",
            new { activityId, timePeriodId = nonexistentPeriodId },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "the validator must refuse this; a 409 means the request reached the database and the foreign key "
            + "caught it, which is a different status from the one a foreign id gets");
        (await CountInPeriodAsync(nonexistentPeriodId)).Should().Be(0);
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

        // On the row, not the status: the point of the control is that the write actually landed, in the
        // caller's own name, so the two rejections above are refusals rather than a broken endpoint.
        await using var check = CreateDbContext();
        var stored = await check.Set<RoutineTodoList>()
            .SingleAsync(r => r.TimePeriodId == periodId, CancellationToken);
        stored.UserId.Should().Be(TodoListTestSeedHelper.TestUserId);
        stored.ActivityId.Should().Be(activityId);
    }

    // ---- suggestion buckets ----------------------------------------------------------------------

    /// <summary>
    /// The whole bucket rule, both fields across all three of the period lengths that decide them: daily
    /// (<c>LengthInDays &lt;= 1</c>) takes neither suggestion, weekly (<c>&lt;= 14</c>) takes days only,
    /// monthly and longer takes day-of-month only.
    /// </summary>
    /// <remarks>
    /// The two accepting rows are what give this teeth — a rule that refused both fields on every length
    /// would satisfy the four rejections on its own and leave the feature quietly unusable.
    /// </remarks>
    [Theory(DisplayName = "Create: each period length admits only its own suggestion field")]
    // Daily: a period that comes round every day has nothing to suggest a day within.
    [InlineData(1, true, false)]
    [InlineData(1, false, false)]
    // Weekly (and up to a fortnight): days of the week, never a day of the month.
    [InlineData(7, true, true)]
    [InlineData(7, false, false)]
    // Monthly and longer: the mirror image.
    [InlineData(30, true, false)]
    [InlineData(30, false, true)]
    public async Task CreateRoutineTodoList_SuggestionFieldMatchesThePeriodBucket(
        int lengthInDays, bool useSuggestedDays, bool expectAccepted)
    {
        long activityId, periodId;
        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "Bucket rule", ct: CancellationToken);
            periodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(
                db, lengthInDays: lengthInDays, ct: CancellationToken);
        }

        object body = useSuggestedDays
            ? new { activityId, timePeriodId = periodId, suggestedDays = new[] { DayOfWeek.Monday } }
            : new { activityId, timePeriodId = periodId, suggestedDayOfMonth = 15 };

        var response = await CreateUserRoleClient().PostAsJsonAsync(
            "/api/routine-todo-list", body, JsonOpts, TestContext.Current.CancellationToken);

        var field = useSuggestedDays ? "SuggestedDays" : "SuggestedDayOfMonth";

        if (expectAccepted)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Created,
                $"{field} is the suggestion a {lengthInDays}-day period is meant to carry");
            (await CountInPeriodAsync(periodId)).Should().Be(1);
        }
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                $"{field} means nothing on a {lengthInDays}-day period");
            (await CountInPeriodAsync(periodId)).Should().Be(0, "a refused create must not leave a row behind");
        }
    }

    /// <summary>
    /// One update-path case, because the rule lives in a single shared implementation now: what this pins is
    /// that `UpdateRoutineTodoListValidator` still calls it at all. The matrix above covers the rule itself.
    /// </summary>
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
