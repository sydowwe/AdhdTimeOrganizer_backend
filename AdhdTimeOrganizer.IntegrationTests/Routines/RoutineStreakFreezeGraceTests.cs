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

namespace AdhdTimeOrganizer.IntegrationTests.Routines;

/// <summary>
/// CQ-13 / CQ-14 — the two ways spending a freeze could charge the user for nothing.
/// <para>
/// Both are silent in the response: the endpoint answers 200, the budget drops, and the damage only shows on
/// the <i>next</i> read. That is why the grace test asserts through a second request rather than on the freeze
/// response alone — the freeze response was always right; it was the page load after it that zeroed the streak.
/// </para>
/// </summary>
[Collection("Postgres")]
public class RoutineStreakFreezeGraceTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    /// <summary>The older miss the user covers — deliberately not the newest entry, which is the case
    /// <c>Apply</c> already handles by clearing the window itself.</summary>
    private static readonly DateOnly OlderMiss = new(2026, 1, 1);

    private static readonly DateOnly EmptyPeriodStart = new(2026, 2, 1);

    private long _periodId;
    private long _emptyPeriodId;

    protected override async Task SeedAsync(DbContext db)
    {
        var period = new RoutineTimePeriod
        {
            UserId = UserId,
            Text = "Grace Freeze Period",
            Color = "#123456",
            LengthInDays = 7,
            ResetAnchorDay = 0,
            StreakThreshold = 100,
            StreakGraceDays = 3,
            // A run of two, and a grace window that lapsed yesterday and nothing has observed yet.
            Streak = 2,
            BestStreak = 3,
            StreakGraceUntil = DateTime.UtcNow.AddDays(-1)
        };

        var emptyPeriod = new RoutineTimePeriod
        {
            UserId = UserId,
            Text = "Empty Freeze Period",
            Color = "#654321",
            // Different length: (UserId, LengthInDays) is unique.
            LengthInDays = 3,
            ResetAnchorDay = 0,
            StreakThreshold = 100,
            StreakGraceDays = 0
        };

        db.Set<RoutineTimePeriod>().AddRange(period, emptyPeriod);
        await db.SaveChangesAsync(CancellationToken);

        db.Set<RoutinePeriodCompletion>().AddRange(
            Completion(period.Id, OlderMiss, 0, 3),
            Completion(period.Id, OlderMiss.AddDays(7), 3, 3),
            Completion(period.Id, OlderMiss.AddDays(14), 3, 3),
            // TotalCount 0: the period elapsed with no items on it at all.
            Completion(emptyPeriod.Id, EmptyPeriodStart, 0, 0));
        await db.SaveChangesAsync(CancellationToken);

        _periodId = period.Id;
        _emptyPeriodId = emptyPeriod.Id;
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

    private Task<HttpResponseMessage> SpendAsync(long periodId, string periodStart) =>
        CreateUserRoleClient().PostAsJsonAsync(
            $"/api/routine-todo-list/time-period/{periodId}/streak-freeze",
            new { periodStart },
            JsonOpts,
            CancellationToken);

    /// <summary>
    /// CQ-13. Freezing an older miss leaves the lapsed window standing unless the endpoint checks grace first —
    /// and <c>RecomputeStreak</c> only ever raises, so the response happily reports the restored run. The next
    /// grouped read then sees the still-set window and zeroes it. The user spends a budgeted resource, watches
    /// it work, and loses the streak on the following page load.
    /// </summary>
    [Fact]
    public async Task SpendFreeze_WithALapsedGraceWindow_ClearsIt_SoTheNextReadKeepsTheRestoredStreak()
    {
        var response = await SpendAsync(_periodId, "2026-01-01");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var served = (await response.Content
            .ReadFromJsonAsync<RoutineTimePeriodResponse>(JsonOpts, CancellationToken))!;
        served.Streak.Should().Be(2, "the two met periods after the frozen miss are the trailing run");

        await using (var db = CreateDbContext())
        {
            var persisted = await db.Set<RoutineTimePeriod>().SingleAsync(p => p.Id == _periodId, CancellationToken);
            persisted.StreakGraceUntil.Should().BeNull(
                "the lapsed window was spent before Apply ran, so nothing is left to break the streak later");
        }

        // The assertion that matters: the streak the user was shown is the streak they still have.
        var groupedResponse = await CreateUserRoleClient()
            .GetAsync("/api/routine-todo-list/grouped-by-time-period", CancellationToken);
        groupedResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var groups = await groupedResponse.Content
            .ReadFromJsonAsync<List<RoutineTodoListGroupedResponse>>(JsonOpts, CancellationToken);

        groups!.Should().ContainSingle(g => g.RoutineTimePeriod.Id == _periodId)
            .Subject.RoutineTimePeriod.Streak.Should().Be(2,
                "the read after the freeze must not undo what the freeze bought");
    }

    /// <summary>
    /// CQ-14. <c>IsSuccess</c> says false for a period that elapsed with no items, but <c>RecomputeStreak</c>
    /// skips those rows outright and <c>TryReset</c> leaves the streak <c>NotEvaluated</c> — so the run already
    /// carries across it for free and a freeze would be charged for a no-op.
    /// </summary>
    [Fact]
    public async Task SpendFreeze_OnAPeriodThatElapsedWithNoItems_Refuses_AndChargesNothing()
    {
        var response = await SpendAsync(_emptyPeriodId, "2026-02-01");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var db = CreateDbContext();
        var period = await db.Set<RoutineTimePeriod>().SingleAsync(p => p.Id == _emptyPeriodId, CancellationToken);
        period.FreezesRemaining.Should().Be(RoutineStreakFreezeService.DefaultFreezeBudget);

        var entry = await db.Set<RoutinePeriodCompletion>()
            .SingleAsync(c => c.TimePeriodId == _emptyPeriodId, CancellationToken);
        entry.IsFrozen.Should().BeFalse();
    }
}
