using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Routines.application.dto.response.todoList;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// CQ-5 / DOC-4 / DOC-7 — the four flat reads that sit on the framework projection bases
/// (<c>GET /routine-time-period</c>, <c>/routine-time-period/{id}</c>, <c>/routine-todo-list</c>,
/// <c>/routine-todo-list/{id}</c>).
/// <para>
/// Every failure these pin is silent. The bases call <c>TResponse.Projection(query)</c> directly, and an
/// expression tree can neither compute <c>NextResetAt</c> / <c>CompletionHistory</c> nor run the two lazy
/// mutations the period's state depends on. So the un-fixed shape returns a perfectly well-formed 200 carrying
/// an epoch <c>NextResetAt</c>, an empty heatmap, a freeze budget nothing ever refills (invariant 9) and a
/// <c>Streak</c> whose grace window lapsed days ago (the "CheckGrace on every access" rule) — a hard <c>0</c>
/// freeze count in particular reads to the client as "no freezes left" rather than "feature absent".
/// </para>
/// <para>
/// Asserted on rows as well as on the response, because both refreshes must be <i>persisted</i>: projecting
/// them alone would hand out a fresh budget on every read and re-break the same streak forever.
/// </para>
/// </summary>
[Collection("Postgres")]
public class RoutineFlatPeriodReadTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly DateOnly FirstStart = new(2026, 1, 1);

    private long _periodId;

    protected override async Task SeedAsync(DbContext db)
    {
        _periodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(
            db, text: "flat-read-period", lengthInDays: 7, resetAnchorDay: 0, historyDepth: 2, ct: CancellationToken);

        // Three rows against a HistoryDepth of 2, so the trim is exercised rather than assumed.
        for (var i = 0; i < 3; i++)
        {
            var start = FirstStart.AddDays(i * 7);
            await TodoListTestSeedHelper.SeedRoutinePeriodCompletionAsync(
                db, _periodId, start, start.AddDays(7), completedCount: 3, totalCount: 3, ct: CancellationToken);
        }
    }

    private async Task<List<RoutineTimePeriodResponse>> GetAllAsync()
    {
        var response = await CreateClient().GetAsync("api/routine-time-period", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content
            .ReadFromJsonAsync<List<RoutineTimePeriodResponse>>(JsonOpts, CancellationToken))!;
    }

    private async Task<RoutineTimePeriodResponse> GetByIdAsync()
    {
        var response = await CreateClient().GetAsync($"api/routine-time-period/{_periodId}", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content
            .ReadFromJsonAsync<RoutineTimePeriodResponse>(JsonOpts, CancellationToken))!;
    }

    /// <summary>
    /// The two members <c>Projection</c> cannot produce. An epoch <c>NextResetAt</c> and an empty
    /// <c>CompletionHistory</c> are the exact shape a raw projection serves, so both are asserted positively.
    /// </summary>
    [Fact]
    public async Task GetAllRoutineTimePeriod_DerivesNextResetAt_AndFillsTheTrimmedHistory()
    {
        var served = (await GetAllAsync()).Should().ContainSingle(p => p.Id == _periodId).Subject;

        served.NextResetAt.Should().NotBe(default, "an epoch NextResetAt is what the raw projection returns");
        served.CompletionHistory.Should().HaveCount(2, "HistoryDepth is 2 and three rows exist");
        served.CompletionHistory.Select(c => c.PeriodStart).Should().Equal(
            FirstStart.AddDays(7), FirstStart.AddDays(14));
    }

    [Fact]
    public async Task GetByIdRoutineTimePeriod_DerivesNextResetAt_AndFillsTheTrimmedHistory()
    {
        var served = await GetByIdAsync();

        served.NextResetAt.Should().NotBe(default);
        served.CompletionHistory.Should().HaveCount(2);
    }

    /// <summary>
    /// Invariant 9 from a fourth surface: nothing refills the budget on a schedule, so a read that shows it owes
    /// the refill. Un-fixed, this read serves the stale <c>0</c> and the user is refused a freeze they are owed.
    /// </summary>
    [Fact]
    public async Task GetAllRoutineTimePeriod_RefillsAnElapsedFreezeWindow_AndPersistsIt()
    {
        await ExhaustBudgetAsync(windowResetsAt: DateTime.UtcNow.AddDays(-1));

        var served = (await GetAllAsync()).Should().ContainSingle(p => p.Id == _periodId).Subject;
        served.FreezesRemaining.Should().Be(RoutineStreakFreezeService.DefaultFreezeBudget);
        served.FreezeBudgetResetsAt.Should().BeAfter(DateTime.UtcNow);

        await using var db = CreateDbContext();
        var persisted = await db.Set<RoutineTimePeriod>().SingleAsync(p => p.Id == _periodId, CancellationToken);
        persisted.FreezesRemaining.Should().Be(RoutineStreakFreezeService.DefaultFreezeBudget,
            "the refill is persisted, not just projected — otherwise every read hands out a fresh budget");
    }

    /// <summary>
    /// The other half of the same rule: a window that has <i>not</i> elapsed must be left alone, or the budget
    /// becomes infinite for anyone who reloads the page.
    /// </summary>
    [Fact]
    public async Task GetAllRoutineTimePeriod_LeavesAnOpenFreezeWindowAlone()
    {
        await ExhaustBudgetAsync(windowResetsAt: RoutineStreakFreezeService.NextBudgetReset(DateTime.UtcNow));

        var served = (await GetAllAsync()).Should().ContainSingle(p => p.Id == _periodId).Subject;
        served.FreezesRemaining.Should().Be(0);
    }

    /// <summary>
    /// DOC-7 from a read path: a lapsed grace window breaks the streak, and if only the nightly job noticed, a
    /// streak would survive indefinitely as long as nobody's period rolled over.
    /// </summary>
    [Fact]
    public async Task GetByIdRoutineTimePeriod_BreaksALapsedGraceWindow_AndPersistsIt()
    {
        await using (var seed = CreateDbContext())
        {
            var period = await seed.Set<RoutineTimePeriod>().SingleAsync(p => p.Id == _periodId, CancellationToken);
            period.Streak = 4;
            period.BestStreak = 4;
            period.StreakGraceUntil = DateTime.UtcNow.AddDays(-1);
            await seed.SaveChangesAsync(CancellationToken);
        }

        var served = await GetByIdAsync();
        served.Streak.Should().Be(0, "the grace window lapsed, so the run is over");
        served.BestStreak.Should().Be(4, "BestStreak records history and is never lowered");

        await using var db = CreateDbContext();
        var persisted = await db.Set<RoutineTimePeriod>().SingleAsync(p => p.Id == _periodId, CancellationToken);
        persisted.Streak.Should().Be(0);
        persisted.StreakGraceUntil.Should().BeNull("a spent window is cleared, not re-evaluated on every read");
    }

    /// <summary>
    /// An open grace window is the whole point of grace — a read must not cut it short.
    /// </summary>
    [Fact]
    public async Task GetByIdRoutineTimePeriod_LeavesAnOpenGraceWindowAlone()
    {
        await using (var seed = CreateDbContext())
        {
            var period = await seed.Set<RoutineTimePeriod>().SingleAsync(p => p.Id == _periodId, CancellationToken);
            period.Streak = 4;
            period.StreakGraceUntil = DateTime.UtcNow.AddDays(2);
            await seed.SaveChangesAsync(CancellationToken);
        }

        (await GetByIdAsync()).Streak.Should().Be(4);
    }

    /// <summary>
    /// The nested period on a routine item. It used to omit the four freeze/reminder columns entirely, which
    /// serialized <c>FreezesRemaining</c> as a hard <c>0</c> — a real value the client reads as "none left"
    /// rather than as "absent".
    /// </summary>
    [Fact]
    public async Task GetAllRoutineTodoList_NestedPeriod_CarriesTheFreezeFieldsAndTheDerivedMembers()
    {
        await ExhaustBudgetAsync(windowResetsAt: DateTime.UtcNow.AddDays(-1));

        long itemId;
        await using (var seed = CreateDbContext())
        {
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(
                seed, "flat-read-activity", ct: CancellationToken);
            itemId = await TodoListTestSeedHelper.SeedRoutineTodoListAsync(
                seed, activityId, _periodId, ct: CancellationToken);
        }

        var response = await CreateClient().GetAsync("api/routine-todo-list", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await response.Content
            .ReadFromJsonAsync<List<RoutineTodoListResponse>>(JsonOpts, CancellationToken);

        var period = items!.Should().ContainSingle(i => i.Id == itemId).Subject.RoutineTimePeriod;
        period.FreezeBudget.Should().Be(RoutineStreakFreezeService.DefaultFreezeBudget);
        period.FreezesRemaining.Should().Be(RoutineStreakFreezeService.DefaultFreezeBudget,
            "the nested period owes the same lazy refill as the top-level one");
        period.FreezeBudgetResetsAt.Should().NotBeNull();
        period.NextResetAt.Should().NotBe(default);
        period.CompletionHistory.Should().HaveCount(2);
    }

    private async Task ExhaustBudgetAsync(DateTime windowResetsAt)
    {
        await using var seed = CreateDbContext();
        var period = await seed.Set<RoutineTimePeriod>().SingleAsync(p => p.Id == _periodId, CancellationToken);
        period.FreezesRemaining = 0;
        period.FreezeBudgetResetsAt = windowResetsAt;
        await seed.SaveChangesAsync(CancellationToken);
    }
}
