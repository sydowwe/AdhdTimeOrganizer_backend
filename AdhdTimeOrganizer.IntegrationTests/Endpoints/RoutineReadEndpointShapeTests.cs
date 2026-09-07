using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Routines.application.dto.response.todoList;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-6 — the *body* the five read endpoints over the framework projection bases serve
/// (<c>GET /routine-time-period</c>, <c>/routine-time-period/{id}</c>,
/// <c>/routine-time-period/all-options/</c>, <c>/routine-todo-list</c>, <c>/routine-todo-list/{id}</c>).
/// <para>
/// Complements <see cref="RoutineFlatPeriodReadTests"/> rather than repeating it. That suite pins the
/// existence of the two derived members and the lazy refresh from the <c>GET all</c> / <c>GET by-id</c>
/// period routes; what is pinned here is the part it leaves open — that <c>NextResetAt</c> is the value
/// <see cref="RoutineResetService.ComputeNextReset"/> computes rather than merely non-epoch, that a
/// hand-set freeze count survives a read untouched (the refill tests can only prove the refilled value,
/// which is also the default), that the grace break happens on the <c>GET all</c> hook too and not just
/// the by-id one, and the item by-id and select-options routes, which nothing reaches today.
/// </para>
/// <para>
/// Auth is deliberately absent: <c>TodoListCrudAuthMatrixTests</c> already covers cross-user get-by-id on
/// both routine entities.
/// </para>
/// </summary>
[Collection("Postgres")]
public class RoutineReadEndpointShapeTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    // Load-bearing: ComputeNextReset is derived from LastResetAt alone, so pinning it to a fixed instant
    // makes the endpoint's NextResetAt a value the test can compute exactly rather than bound.
    private static readonly DateTime LastReset = new(2026, 2, 3, 9, 30, 0, DateTimeKind.Utc);

    private static readonly DateOnly FirstStart = new(2026, 1, 5);

    private long _periodId;
    private long _itemId;

    protected override async Task SeedAsync(DbContext db)
    {
        var period = new RoutineTimePeriod
        {
            UserId = UserId,
            Text = "shape-period",
            Color = "#123456",
            LengthInDays = 7,
            // Rolling, so ck_routine_time_period_reset_anchor_day_range admits it and ComputeNextReset
            // takes its short path — LastResetAt + LengthInDays, truncated to the date.
            ResetAnchorDay = 0,
            StreakThreshold = 90,
            StreakGraceDays = 0,
            LastResetAt = LastReset,
            // Non-default on purpose: a projection that ignored HistoryDepth would still pass at 16.
            HistoryDepth = 3,
            // Must stay < LengthInDays. Serves as the round-trip probe on the nested period, where this
            // column used to be omitted outright.
            ReminderLeadDays = 2,
            // Hand-set and deliberately NOT the default budget: 1 of 2 is a state only a read that serves
            // the row can produce — the refill path would report 2.
            FreezesRemaining = 1,
            FreezeBudget = 2,
            // Window still open, so the lazy refill must leave the 1 alone.
            FreezeBudgetResetsAt = RoutineStreakFreezeService.NextBudgetReset(DateTime.UtcNow)
        };
        db.Set<RoutineTimePeriod>().Add(period);
        await db.SaveChangesAsync(CancellationToken);
        _periodId = period.Id;

        await TodoListTestSeedHelper.SeedRoutinePeriodCompletionAsync(
            db, _periodId, FirstStart, FirstStart.AddDays(7), completedCount: 3, totalCount: 3, ct: CancellationToken);
        await TodoListTestSeedHelper.SeedRoutinePeriodCompletionAsync(
            db, _periodId, FirstStart.AddDays(7), FirstStart.AddDays(14), completedCount: 3, totalCount: 3,
            ct: CancellationToken);

        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "shape-activity", ct: CancellationToken);
        _itemId = await TodoListTestSeedHelper.SeedRoutineTodoListAsync(
            db, activityId, _periodId, ct: CancellationToken);
    }

    private async Task<List<RoutineTimePeriodResponse>> GetAllPeriodsAsync()
    {
        var response = await CreateClient().GetAsync("api/routine-time-period", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content
            .ReadFromJsonAsync<List<RoutineTimePeriodResponse>>(JsonOpts, CancellationToken))!;
    }

    private async Task<RoutineTimePeriodResponse> GetPeriodByIdAsync()
    {
        var response = await CreateClient().GetAsync($"api/routine-time-period/{_periodId}", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content
            .ReadFromJsonAsync<RoutineTimePeriodResponse>(JsonOpts, CancellationToken))!;
    }

    /// <summary>
    /// The exact oracle rather than "not epoch": <c>ComputeNextReset</c> ignores <c>now</c> and derives the
    /// boundary from <c>LastResetAt</c> alone, so the served value is reproducible to the tick. A read that
    /// re-derived it from the wrong anchor would still clear a <c>NotBe(default)</c> check.
    /// </summary>
    [Fact]
    public async Task GetAllRoutineTimePeriod_ServesTheNextResetAtTheDomainServiceComputes()
    {
        var served = (await GetAllPeriodsAsync()).Should().ContainSingle(p => p.Id == _periodId).Subject;

        await using var db = CreateDbContext();
        var period = await db.Set<RoutineTimePeriod>().SingleAsync(p => p.Id == _periodId, CancellationToken);

        served.NextResetAt.Should().Be(RoutineResetService.ComputeNextReset(period, DateTime.UtcNow));
        served.NextResetAt.Should().Be(LastReset.AddDays(7).Date, "a rolling anchor truncates to the date");
    }

    [Fact]
    public async Task GetByIdRoutineTimePeriod_ServesTheNextResetAtTheDomainServiceComputes()
    {
        var served = await GetPeriodByIdAsync();

        await using var db = CreateDbContext();
        var period = await db.Set<RoutineTimePeriod>().SingleAsync(p => p.Id == _periodId, CancellationToken);

        served.NextResetAt.Should().Be(RoutineResetService.ComputeNextReset(period, DateTime.UtcNow));
    }

    /// <summary>
    /// A partially spent budget inside an open window. Both numbers are non-default, so this fails on a
    /// response that omits the columns (which serializes them as a hard <c>0</c> the client reads as "no
    /// freezes left") and on one that refills a window it should not have touched.
    /// </summary>
    [Fact]
    public async Task GetAllRoutineTimePeriod_ServesTheSeededFreezeCounts()
    {
        var served = (await GetAllPeriodsAsync()).Should().ContainSingle(p => p.Id == _periodId).Subject;

        served.FreezesRemaining.Should().Be(1);
        served.FreezeBudget.Should().Be(2);
        served.FreezeBudgetResetsAt.Should().NotBeNull();
    }

    /// <summary>
    /// The "CheckGrace on every access" rule from the <c>GET all</c> hook. <c>RoutineFlatPeriodReadTests</c>
    /// pins it on by-id; the two endpoints override different hooks
    /// (<c>PostProcessItemsAsync</c> vs <c>PostProcessAsync</c>), so neither implies the other.
    /// </summary>
    [Fact]
    public async Task GetAllRoutineTimePeriod_BreaksALapsedGraceWindow_AndPersistsIt()
    {
        await using (var seed = CreateDbContext())
        {
            var period = await seed.Set<RoutineTimePeriod>().SingleAsync(p => p.Id == _periodId, CancellationToken);
            period.Streak = 4;
            period.BestStreak = 4;
            period.StreakGraceUntil = DateTime.UtcNow.AddDays(-1);
            await seed.SaveChangesAsync(CancellationToken);
        }

        var served = (await GetAllPeriodsAsync()).Should().ContainSingle(p => p.Id == _periodId).Subject;
        served.Streak.Should().Be(0, "the grace window lapsed, so the run is over");
        served.BestStreak.Should().Be(4, "BestStreak records history and is never lowered");

        await using var db = CreateDbContext();
        var persisted = await db.Set<RoutineTimePeriod>().SingleAsync(p => p.Id == _periodId, CancellationToken);
        persisted.Streak.Should().Be(0, "the break is persisted, not just projected, or the next read re-breaks it");
        persisted.StreakGraceUntil.Should().BeNull("a spent window is cleared, not re-evaluated on every read");
    }

    /// <summary>
    /// The item by-id route, which nothing else reaches. Its nested period is built by hand inside an
    /// expression tree, so it is the shape most likely to silently drop a column — <c>ReminderLeadDays</c>
    /// and the three freeze fields are asserted individually rather than as a blanket "not null".
    /// </summary>
    [Fact]
    public async Task GetByIdRoutineTodoList_NestedPeriod_CarriesTheFreezeFieldsAndTheDerivedMembers()
    {
        var response = await CreateClient().GetAsync($"api/routine-todo-list/{_itemId}", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var item = (await response.Content
            .ReadFromJsonAsync<RoutineTodoListResponse>(JsonOpts, CancellationToken))!;

        var period = item.RoutineTimePeriod;
        period.Id.Should().Be(_periodId);
        period.FreezesRemaining.Should().Be(1, "a hard 0 reads to the client as 'no freezes left', not 'absent'");
        period.FreezeBudget.Should().Be(2);
        period.FreezeBudgetResetsAt.Should().NotBeNull();
        period.ReminderLeadDays.Should().Be(2, "the column used to be omitted from the nested projection");
        period.CompletionHistory.Should().HaveCount(2);

        await using var db = CreateDbContext();
        var persisted = await db.Set<RoutineTimePeriod>().SingleAsync(p => p.Id == _periodId, CancellationToken);
        period.NextResetAt.Should().Be(RoutineResetService.ComputeNextReset(persisted, DateTime.UtcNow),
            "the nested period owes the same derived members as the top-level one");
    }

    /// <summary>
    /// The one thing these endpoints customise: <c>GetSelectOptionsRoutineTimePeriodEndpoint</c> overrides
    /// <c>Sort</c> to order by <c>LengthInDays</c> rather than the base's id order. Seeded out of both id and
    /// text order so neither can produce the expected sequence by accident.
    /// </summary>
    [Fact]
    public async Task SelectOptions_AreSortedByLengthInDays()
    {
        long thirtyId, oneId, fiveId;
        await using (var seed = CreateDbContext())
        {
            // Insertion order 30, 1, 5 — i.e. ascending id is neither the expected order nor its reverse.
            // Lengths avoid the fixture period's 7: (user_id, length_in_days) is unique.
            thirtyId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(
                seed, text: "opt-thirty", lengthInDays: 30, resetAnchorDay: 0, ct: CancellationToken);
            oneId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(
                seed, text: "opt-one", lengthInDays: 1, resetAnchorDay: 0, ct: CancellationToken);
            fiveId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(
                seed, text: "opt-five", lengthInDays: 5, resetAnchorDay: 0, ct: CancellationToken);
        }

        var response = await CreateClient().GetAsync("api/routine-time-period/all-options/", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var options = (await response.Content
            .ReadFromJsonAsync<List<SelectOptionResponse>>(JsonOpts, CancellationToken))!;

        options.Select(o => o.Id).Should().Contain([oneId, fiveId, thirtyId]);
        // Relative order of the three seeded here, so an unrelated period cannot perturb the assertion.
        options.Select(o => o.Id).Where(id => id == oneId || id == fiveId || id == thirtyId)
            .Should().Equal(oneId, fiveId, thirtyId);
        options.Should().ContainSingle(o => o.Id == oneId).Which.Text.Should().Be("opt-one");
    }

    /// <summary>
    /// <c>ChangeDisplayOrderRoutineTodoListEndpoint</c> sets <c>GroupFilterExpression =&gt; e.TimePeriodId</c>;
    /// the existing reorder coverage all targets <c>todo-list-item</c>. A group filter that resolved to the
    /// wrong column would reorder across every period the user owns and nothing would report an error.
    /// </summary>
    [Fact]
    public async Task ChangeDisplayOrder_ReordersWithinThePeriodOnly()
    {
        long otherPeriodId, a1, a2, b1, b2;
        await using (var seed = CreateDbContext())
        {
            // 14, not 7: (user_id, length_in_days) is unique, and 14 % 7 == 0 keeps ResetAnchorDay 0 legal.
            otherPeriodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(
                seed, text: "reorder-other-period", lengthInDays: 14, resetAnchorDay: 0, ct: CancellationToken);

            // A distinct activity per item: (user_id, time_period_id, activity_id) is unique.
            a1 = await SeedItemAsync(seed, _periodId, "reorder-a1", displayOrder: 1000);
            a2 = await SeedItemAsync(seed, _periodId, "reorder-a2", displayOrder: 2000);
            b1 = await SeedItemAsync(seed, otherPeriodId, "reorder-b1", displayOrder: 1000);
            b2 = await SeedItemAsync(seed, otherPeriodId, "reorder-b2", displayOrder: 2000);
        }

        // Move a1 to the bottom of its own period, i.e. after a2.
        var response = await CreateClient().PatchAsJsonAsync("api/routine-todo-list/change-display-order", new
        {
            MovedItemId = a1,
            PrecedingItemId = (long?)a2,
            FollowingItemId = (long?)null
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = CreateDbContext();
        var orders = await db.Set<RoutineTodoList>()
            .Where(i => i.Id == a1 || i.Id == a2 || i.Id == b1 || i.Id == b2)
            .ToDictionaryAsync(i => i.Id, i => i.DisplayOrder, CancellationToken);

        orders[a1].Should().BeGreaterThan(orders[a2], "a1 was dropped after a2");
        orders[a2].Should().Be(2000, "only the moved item's order is rewritten");

        orders[b1].Should().Be(1000, "the other period is a separate group and must not be touched");
        orders[b2].Should().Be(2000, "the other period is a separate group and must not be touched");
    }

    /// <summary>
    /// The rebalance path, which is the only place <c>GroupFilterExpression</c> is used as a <i>filter</i>
    /// rather than as a projection — <c>GetGroupIdById</c> feeds it to <c>Select</c>, which translates, but
    /// the re-index feeds it to <c>Where</c>.
    /// <para>
    /// Reached by dropping an item between two neighbours with no integer gap between them (1000 and 1001):
    /// the midpoint equals the preceding order, so the endpoint re-indexes the whole group and retries. This
    /// is the branch a user hits after enough reorders in one list, and it is unreachable from the assertions
    /// above, which always leave a gap.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ChangeDisplayOrder_WithNoGapBetweenNeighbours_RebalancesTheGroupAndCompletesTheMove()
    {
        long otherPeriodId, a1, a2, a3, b1;
        await using (var seed = CreateDbContext())
        {
            otherPeriodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(
                seed, text: "rebalance-other-period", lengthInDays: 14, resetAnchorDay: 0, ct: CancellationToken);

            // Adjacent orders: (1001 - 1000) / 2 == 0, so the midpoint lands back on a1 and forces a rebalance.
            a1 = await SeedItemAsync(seed, _periodId, "rebalance-a1", displayOrder: 1000);
            a2 = await SeedItemAsync(seed, _periodId, "rebalance-a2", displayOrder: 1001);
            a3 = await SeedItemAsync(seed, _periodId, "rebalance-a3", displayOrder: 5000);
            b1 = await SeedItemAsync(seed, otherPeriodId, "rebalance-b1", displayOrder: 1000);
        }

        var response = await CreateClient().PatchAsJsonAsync("api/routine-todo-list/change-display-order", new
        {
            MovedItemId = a3,
            PrecedingItemId = (long?)a1,
            FollowingItemId = (long?)a2
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = CreateDbContext();
        var orders = await db.Set<RoutineTodoList>()
            .Where(i => i.Id == a1 || i.Id == a2 || i.Id == a3 || i.Id == b1)
            .ToDictionaryAsync(i => i.Id, i => i.DisplayOrder, CancellationToken);

        orders[a3].Should().BeGreaterThan(orders[a1]).And.BeLessThan(orders[a2],
            "the move is what the rebalance exists to make room for, so it must land between its neighbours");
        orders.Values.Distinct().Should().HaveCount(4, "a re-index that left a collision would defeat itself");

        orders[b1].Should().Be(1000, "the rebalance re-indexes one group, not every item the user owns");
    }

    private static async Task<long> SeedItemAsync(DbContext db, long periodId, string name, long displayOrder)
    {
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, name, ct: CancellationToken);
        var itemId = await TodoListTestSeedHelper.SeedRoutineTodoListAsync(
            db, activityId, periodId, ct: CancellationToken);

        var item = await db.Set<RoutineTodoList>().SingleAsync(i => i.Id == itemId, CancellationToken);
        item.DisplayOrder = displayOrder;
        await db.SaveChangesAsync(CancellationToken);
        return itemId;
    }
}
