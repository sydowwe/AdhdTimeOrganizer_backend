using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.Routines.application.dto.response.todoList;
using FluentAssertions;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-4 / Section H — <c>GetCompletionHistoryRoutineTimePeriodEndpoint</c>
/// (<c>GET /api/routine-time-period/{id}/completion-history</c>) returns
/// <c>RoutinePeriodCompletion</c> rows bounded by the period's own <c>HistoryDepth</c>. The handler orders
/// by <c>PeriodStart</c> descending, takes <c>HistoryDepth</c>, then reverses — so the cap keeps the
/// <i>newest</i> <c>HistoryDepth</c> rows, but the list actually comes back oldest-first (matching the
/// endpoint's own summary text, "sorted oldest to newest" — the spec's "newest-first" wording describes
/// which rows survive the cap, not the wire order).
/// </summary>
[Collection("Postgres")]
public class RoutinePeriodCompletionHistoryTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    [Fact(DisplayName = "More completions than HistoryDepth: cap is applied and the kept rows are the newest, returned oldest-first")]
    public async Task History_MoreCompletionsThanDepth_CapsToNewestAndReturnsOldestFirst()
    {
        const int historyDepth = 3;
        long periodId;
        await using (var db = CreateDbContext())
        {
            periodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, text: "history-cap-period", historyDepth: historyDepth);

            // Five completions, each period one week after the last -- more than HistoryDepth.
            var start = new DateOnly(2026, 1, 1);
            for (var i = 0; i < 5; i++)
            {
                var periodStart = start.AddDays(i * 7);
                await TodoListTestSeedHelper.SeedRoutinePeriodCompletionAsync(
                    db, periodId, periodStart, periodStart.AddDays(7), completedCount: i, totalCount: 5);
            }
        }

        var response = await CreateClient().GetAsync($"api/routine-time-period/{periodId}/completion-history", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var records = await response.Content.ReadFromJsonAsync<List<PeriodCompletionRecord>>(JsonOpts, CancellationToken);
        records.Should().HaveCount(historyDepth, "the response is capped to HistoryDepth even though 5 completions exist");

        // The newest 3 completions have PeriodStart 2026-01-15, 01-22, 01-29 (i = 2, 3, 4); oldest-first order.
        records!.Select(r => r.PeriodStart).Should().Equal(
            new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 22), new DateOnly(2026, 1, 29));
    }

    [Fact(DisplayName = "User B cannot read user A's completion history (404)")]
    public async Task History_ForeignPeriod_Returns404()
    {
        long otherPeriodId;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            otherPeriodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(
                db, ReminderSeedHelper.OtherUserId, text: "history-idor-period");
            await TodoListTestSeedHelper.SeedRoutinePeriodCompletionAsync(
                db, otherPeriodId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 8));
        }

        var response = await CreateClient().GetAsync($"api/routine-time-period/{otherPeriodId}/completion-history", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the period lookup is scoped to the caller's UserId, so a foreign id resolves to nothing");
    }
}
