using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.Routines.application.dto.response.todoList;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Routines;

/// <summary>
/// The streak freeze endpoint, end to end.
/// <para>
/// Everything here asserts on rows and on the recomputed streak rather than on the route answering, because
/// each failure mode is silent: a freeze that decrements the budget without flagging the completion row leaves
/// the heatmap showing a miss and the user out one freeze; a recompute that skips the frozen entry leaves the
/// streak at zero while the response still says 200; and the budget refill has no job behind it, so a read
/// path that forgets to refresh refuses a freeze the user is owed with a perfectly ordinary 400.
/// </para>
/// </summary>
[Collection("Postgres")]
public class RoutineStreakFreezeTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    // The UserId FK is NOT NULL and real — an IDOR fixture needs an actual second user row, not just an id
    // nobody owns.
    private static long OtherUserId => ReminderSeedHelper.OtherUserId;

    private static readonly DateOnly MissStart = new(2026, 1, 22);

    private long _periodId;

    protected override async Task SeedAsync(DbContext db)
    {
        var period = new RoutineTimePeriod
        {
            UserId = UserId,
            Text = "Freeze Period",
            Color = "#123456",
            LengthInDays = 7,
            ResetAnchorDay = 0,
            StreakThreshold = 100,
            StreakGraceDays = 0,
            // What the reset job left behind after the miss below broke the run.
            Streak = 0,
            BestStreak = 3
        };
        db.Set<RoutineTimePeriod>().Add(period);
        await db.SaveChangesAsync(CancellationToken);

        db.Set<RoutinePeriodCompletion>().AddRange(
            Completion(period.Id, new DateOnly(2026, 1, 1), 3, 3),
            Completion(period.Id, new DateOnly(2026, 1, 8), 3, 3),
            Completion(period.Id, new DateOnly(2026, 1, 15), 3, 3),
            Completion(period.Id, MissStart, 1, 3));
        await db.SaveChangesAsync(CancellationToken);

        _periodId = period.Id;
    }

    private static RoutinePeriodCompletion Completion(long periodId, DateOnly start, int completed, int total) =>
        new()
        {
            TimePeriodId = periodId,
            PeriodStart = start,
            PeriodEnd = start.AddDays(7),
            CompletedCount = completed,
            TotalCount = total
        };

    private Task<HttpResponseMessage> SpendAsync(HttpClient client, string periodStart, long? periodId = null) =>
        client.PostAsJsonAsync(
            $"/api/routine-todo-list/time-period/{periodId ?? _periodId}/streak-freeze",
            new { periodStart },
            JsonOpts,
            CancellationToken);

    private async Task<RoutineTimePeriodResponse> SpendOkAsync(HttpClient client, string periodStart)
    {
        var response = await SpendAsync(client, periodStart);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<RoutineTimePeriodResponse>(JsonOpts, CancellationToken))!;
    }

    /// <summary>
    /// The whole contract in one pass: the streak survives the miss, the budget drops by one, and the covered
    /// entry comes back flagged — all three read off the single response the client re-parses.
    /// </summary>
    [Fact]
    public async Task SpendFreeze_CarriesTheStreakAcrossTheMiss_AndSpendsOneFreeze()
    {
        var client = CreateUserRoleClient();

        var period = await SpendOkAsync(client, "2026-01-22");

        period.Streak.Should().Be(3, "the three met periods before the miss are one unbroken run once it is frozen");
        period.FreezesRemaining.Should().Be(RoutineStreakFreezeService.DefaultFreezeBudget - 1);
        period.FreezeBudget.Should().Be(RoutineStreakFreezeService.DefaultFreezeBudget);
        period.FreezeBudgetResetsAt.Should().NotBeNull();

        period.CompletionHistory.Should().ContainSingle(c => c.PeriodStart == MissStart)
            .Which.IsFrozen.Should().BeTrue();
        period.CompletionHistory.Where(c => c.PeriodStart != MissStart)
            .Should().OnlyContain(c => !c.IsFrozen);
    }

    /// <summary>
    /// The counts on a frozen period are left exactly as recorded — a freeze changes the scoring, not the
    /// history. If it ever starts rewriting them, the completion rate silently stops being the truth.
    /// </summary>
    [Fact]
    public async Task SpendFreeze_LeavesTheRecordedCountsAlone()
    {
        var client = CreateUserRoleClient();

        await SpendOkAsync(client, "2026-01-22");

        await using var db = CreateDbContext();
        var entry = await db.Set<RoutinePeriodCompletion>()
            .SingleAsync(c => c.TimePeriodId == _periodId && c.PeriodStart == MissStart, CancellationToken);

        entry.CompletedCount.Should().Be(1);
        entry.TotalCount.Should().Be(3);
        entry.IsFrozen.Should().BeTrue();
        entry.FrozenAt.Should().NotBeNull("ck_routine_period_completion_frozen_at_matches_is_frozen pairs the two");
    }

    /// <summary>
    /// The client round-trips <c>periodStart</c> through a JavaScript <c>Date</c>, so it comes back as a full
    /// instant rather than the date it was served as. A DateOnly binder would 400 on it.
    /// </summary>
    [Fact]
    public async Task SpendFreeze_AcceptsAFullIsoInstant_NotJustADate()
    {
        var client = CreateUserRoleClient();

        var period = await SpendOkAsync(client, "2026-01-22T00:00:00.000Z");

        period.CompletionHistory.Should().ContainSingle(c => c.PeriodStart == MissStart)
            .Which.IsFrozen.Should().BeTrue();
    }

    [Fact]
    public async Task SpendFreeze_Twice_RefusesTheSecond_AndSpendsOnlyOne()
    {
        var client = CreateUserRoleClient();

        await SpendOkAsync(client, "2026-01-22");
        var second = await SpendAsync(client, "2026-01-22");

        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var db = CreateDbContext();
        var period = await db.Set<RoutineTimePeriod>().SingleAsync(p => p.Id == _periodId, CancellationToken);
        period.FreezesRemaining.Should().Be(RoutineStreakFreezeService.DefaultFreezeBudget - 1);
    }

    /// <summary>
    /// Covering a period that met its threshold would burn a freeze for nothing. The client never offers it,
    /// so reaching here means a stale view — which deserves an error rather than a charge.
    /// </summary>
    [Fact]
    public async Task SpendFreeze_OnAPeriodThatMetItsThreshold_Refuses()
    {
        var client = CreateUserRoleClient();

        var response = await SpendAsync(client, "2026-01-08");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var db = CreateDbContext();
        var period = await db.Set<RoutineTimePeriod>().SingleAsync(p => p.Id == _periodId, CancellationToken);
        period.FreezesRemaining.Should().Be(RoutineStreakFreezeService.DefaultFreezeBudget);
    }

    [Fact]
    public async Task SpendFreeze_WithAnExhaustedBudget_RefusesAndLeavesTheEntryUnfrozen()
    {
        await using (var seed = CreateDbContext())
        {
            var period = await seed.Set<RoutineTimePeriod>().SingleAsync(p => p.Id == _periodId, CancellationToken);
            period.FreezesRemaining = 0;
            // Window still open, so the lazy refill must not top it back up.
            period.FreezeBudgetResetsAt = RoutineStreakFreezeService.NextBudgetReset(DateTime.UtcNow);
            await seed.SaveChangesAsync(CancellationToken);
        }

        var response = await SpendAsync(CreateUserRoleClient(), "2026-01-22");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var db = CreateDbContext();
        var entry = await db.Set<RoutinePeriodCompletion>()
            .SingleAsync(c => c.TimePeriodId == _periodId && c.PeriodStart == MissStart, CancellationToken);
        entry.IsFrozen.Should().BeFalse();
    }

    /// <summary>
    /// The budget refill is lazy — no job fires it. Spending after the window has elapsed must find a full
    /// budget, or a user who did not open the app for a month keeps the exhausted count forever.
    /// </summary>
    [Fact]
    public async Task SpendFreeze_AfterTheWindowElapsed_RefillsTheBudgetFirst()
    {
        await using (var seed = CreateDbContext())
        {
            var period = await seed.Set<RoutineTimePeriod>().SingleAsync(p => p.Id == _periodId, CancellationToken);
            period.FreezesRemaining = 0;
            period.FreezeBudgetResetsAt = DateTime.UtcNow.AddDays(-1);
            await seed.SaveChangesAsync(CancellationToken);
        }

        var period2 = await SpendOkAsync(CreateUserRoleClient(), "2026-01-22");

        period2.FreezesRemaining.Should().Be(RoutineStreakFreezeService.DefaultFreezeBudget - 1);
        period2.FreezeBudgetResetsAt.Should().BeAfter(DateTime.UtcNow);
    }

    /// <summary>
    /// Ownership, asserted through a second user rather than through the global query filter: the endpoint
    /// filters on UserId by hand precisely so the 404 does not depend on which filters happen to be installed.
    /// </summary>
    [Fact]
    public async Task SpendFreeze_OnAnotherUsersPeriod_IsNotFound()
    {
        long otherPeriodId;
        await using (var seed = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(seed, CancellationToken);

            var period = new RoutineTimePeriod
            {
                UserId = OtherUserId,
                Text = "Someone else's period",
                Color = "#654321",
                // 7, not 14: ck_routine_time_period_reset_anchor_day_range only admits a rolling anchor (0) on
                // periods of 7 days or fewer. The unique (user_id, length_in_days) index is per-user, so
                // sharing the length with the fixture period above is fine.
                LengthInDays = 7,
                ResetAnchorDay = 0,
                StreakThreshold = 100,
                StreakGraceDays = 0
            };
            seed.Set<RoutineTimePeriod>().Add(period);
            await seed.SaveChangesAsync(CancellationToken);

            seed.Set<RoutinePeriodCompletion>().Add(Completion(period.Id, MissStart, 0, 3));
            await seed.SaveChangesAsync(CancellationToken);
            otherPeriodId = period.Id;
        }

        var response = await SpendAsync(CreateUserRoleClient(), "2026-01-22", otherPeriodId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SpendFreeze_ForAPeriodStartWithNoHistoryEntry_IsNotFound()
    {
        var response = await SpendAsync(CreateUserRoleClient(), "2025-06-03");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The grouped read is what renders the budget chip and the heatmap, so it has to carry the same fields the
    /// freeze endpoint returns — and it is the read that refreshes an elapsed budget window on the way past.
    /// </summary>
    [Fact]
    public async Task GroupedRead_ServesTheFreezeFields_AndRefillsAnElapsedWindow()
    {
        await using (var seed = CreateDbContext())
        {
            var period = await seed.Set<RoutineTimePeriod>().SingleAsync(p => p.Id == _periodId, CancellationToken);
            period.FreezesRemaining = 0;
            period.FreezeBudgetResetsAt = DateTime.UtcNow.AddDays(-1);
            await seed.SaveChangesAsync(CancellationToken);
        }

        var client = CreateUserRoleClient();
        var response = await client.GetAsync("/api/routine-todo-list/grouped-by-time-period", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var groups = await response.Content
            .ReadFromJsonAsync<List<RoutineTodoListGroupedResponse>>(JsonOpts, CancellationToken);

        var served = groups!.Should().ContainSingle(g => g.RoutineTimePeriod.Id == _periodId).Subject.RoutineTimePeriod;
        served.FreezesRemaining.Should().Be(RoutineStreakFreezeService.DefaultFreezeBudget);
        served.FreezeBudget.Should().Be(RoutineStreakFreezeService.DefaultFreezeBudget);
        served.CompletionHistory.Should().HaveCount(4).And.OnlyContain(c => !c.IsFrozen);

        await using var db = CreateDbContext();
        var persisted = await db.Set<RoutineTimePeriod>().SingleAsync(p => p.Id == _periodId, CancellationToken);
        persisted.FreezesRemaining.Should().Be(RoutineStreakFreezeService.DefaultFreezeBudget,
            "the refill is persisted, not just projected — otherwise every read hands out a fresh budget");
    }
}
