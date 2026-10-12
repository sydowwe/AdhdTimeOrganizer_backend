using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.rule;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Routines.application.dto.response.todoList;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// F2-06: each routine item's <c>typicalSeconds</c> / <c>sampleCount</c>, on every read that returns one.
/// <para>
/// The time crosses from History through <c>IRoutineItemLoggedTimeSource</c>, and every way this goes wrong still
/// answers 200 with a plausible number: keyed on the activity, two items sharing one would each get the other's
/// time; sampled per sitting, an item done in two sittings would read as half its length; with the open cycle always
/// counted, the figure would sink each morning. So each case asserts on the figure a seeded history produces. Rows
/// are written straight through the DbContext, which publishes no <c>ActivityTimeRecordedEvent</c>.
/// </para>
/// </summary>
[Collection("Postgres")]
public class RoutineItemTypicalDurationTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    /// <summary>The current cycle of every weekly period here opened two days ago and closes in five.</summary>
    private static readonly DateTime LastReset = DateTime.UtcNow.Date.AddDays(-2);

    /// <summary>A start inside the <paramref name="cyclesAgo"/>-th closed week (1 = the newest), on its second day.</summary>
    private static DateTime InClosedCycle(int cyclesAgo, int hour = 9) =>
        LastReset.AddDays(-7 * cyclesAgo + 1).AddHours(hour);

    [Fact(DisplayName = "F2-06: the figure is the median occurrence, every sitting of a cycle summed first")]
    public async Task Typical_IsTheMedianOccurrence()
    {
        var (periodId, _) = await SeedWeeklyPeriodAsync(closedCycles: 3);
        var (itemId, activityId) = await SeedItemAsync(periodId, "occurrence");

        // 10 + 8 minutes in the newest week, then 20 and 15: occurrences of 18, 20 and 15 minutes.
        await LogAsync(activityId, itemId, InClosedCycle(1), 10 * 60);
        await LogAsync(activityId, itemId, InClosedCycle(1, hour: 18), 8 * 60);
        await LogAsync(activityId, itemId, InClosedCycle(2), 20 * 60);
        await LogAsync(activityId, itemId, InClosedCycle(3), 15 * 60);

        var item = await GroupedItemAsync(itemId);

        item.TypicalSeconds.Should().Be(18 * 60, "a per-sitting median would say 12.5 minutes");
        item.SampleCount.Should().Be(3);
    }

    [Fact(DisplayName = "F2-06: below three timed occurrences the item carries nothing")]
    public async Task BelowTheThreshold_IsNull()
    {
        var (periodId, _) = await SeedWeeklyPeriodAsync(closedCycles: 3);
        var (itemId, activityId) = await SeedItemAsync(periodId, "thin");
        for (var cycle = 1; cycle < EstimateFitRule.RoutineItemMinimumSamples; cycle++)
            await LogAsync(activityId, itemId, InClosedCycle(cycle), 20 * 60);

        var item = await GroupedItemAsync(itemId);

        item.TypicalSeconds.Should().BeNull();
        item.SampleCount.Should().BeNull();
    }

    /// <summary>
    /// The reason the item link exists. Two routine items on one activity (in two periods), plus an unlinked
    /// recording of that activity — the kind the tracker heartbeat writes — in every cycle.
    /// </summary>
    [Fact(DisplayName = "F2-06: only time logged against the item counts, not time on its activity")]
    public async Task Time_IsKeyedOnTheItem_NotTheActivity()
    {
        var (weeklyId, _) = await SeedWeeklyPeriodAsync(closedCycles: 3);
        var (fortnightlyId, _) = await SeedWeeklyPeriodAsync(closedCycles: 0, lengthInDays: 14);
        var (itemId, activityId) = await SeedItemAsync(weeklyId, "shared");
        long siblingId;
        await using (var db = CreateDbContext())
            siblingId = await TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, activityId, fortnightlyId, ct: CancellationToken);

        for (var cycle = 1; cycle <= 3; cycle++)
        {
            await LogAsync(activityId, itemId, InClosedCycle(cycle), 20 * 60);
            await LogAsync(activityId, null, InClosedCycle(cycle, hour: 15), 3 * 3600);
        }

        var groups = await GroupedAsync();
        var item = groups.SelectMany(g => g.Items).Single(i => i.Id == itemId);
        var sibling = groups.SelectMany(g => g.Items).Single(i => i.Id == siblingId);

        item.TypicalSeconds.Should().Be(20 * 60);
        sibling.TypicalSeconds.Should().BeNull("nothing was logged against the sibling item itself");
    }

    [Fact(DisplayName = "F2-06: this cycle's time counts once the item is done, and not while it is still open")]
    public async Task OpenCycle_CountsOnlyWhenDone()
    {
        var (periodId, _) = await SeedWeeklyPeriodAsync(closedCycles: 2);
        var (doneId, doneActivity) = await SeedItemAsync(periodId, "done-now", isDone: true);
        var (openId, openActivity) = await SeedItemAsync(periodId, "still-open");

        foreach (var (itemId, activityId) in new[] { (doneId, doneActivity), (openId, openActivity) })
        {
            await LogAsync(activityId, itemId, InClosedCycle(1), 20 * 60);
            await LogAsync(activityId, itemId, InClosedCycle(2), 30 * 60);
            await LogAsync(activityId, itemId, LastReset.AddHours(9), 40 * 60);
        }

        var groups = await GroupedAsync();
        var done = groups.SelectMany(g => g.Items).Single(i => i.Id == doneId);
        var open = groups.SelectMany(g => g.Items).Single(i => i.Id == openId);

        done.TypicalSeconds.Should().Be(30 * 60);
        done.SampleCount.Should().Be(3);
        open.TypicalSeconds.Should().BeNull("two finished occurrences, and this week's is still running");
    }

    /// <summary>
    /// The routine list re-reads one item by id after time is logged against it, so a figure only the grouped read
    /// carried would vanish from the row the moment the user logged more.
    /// </summary>
    [Fact(DisplayName = "F2-06: the flat reads carry the figure too")]
    public async Task FlatReads_CarryTheFigure()
    {
        var (periodId, _) = await SeedWeeklyPeriodAsync(closedCycles: 3);
        var (itemId, activityId) = await SeedItemAsync(periodId, "flat");
        for (var cycle = 1; cycle <= 3; cycle++)
            await LogAsync(activityId, itemId, InClosedCycle(cycle), 25 * 60);

        var client = CreateClient();

        var byIdResponse = await client.GetAsync($"api/routine-todo-list/{itemId}", CancellationToken);
        byIdResponse.StatusCode.Should().Be(HttpStatusCode.OK, await byIdResponse.Content.ReadAsStringAsync(CancellationToken));
        var byId = await byIdResponse.Content.ReadFromJsonAsync<RoutineTodoListResponse>(JsonOpts, CancellationToken);
        byId!.TypicalSeconds.Should().Be(25 * 60);
        byId.SampleCount.Should().Be(3);

        var allResponse = await client.GetAsync("api/routine-todo-list", CancellationToken);
        allResponse.StatusCode.Should().Be(HttpStatusCode.OK, await allResponse.Content.ReadAsStringAsync(CancellationToken));
        var all = await allResponse.Content.ReadFromJsonAsync<List<RoutineTodoListResponse>>(JsonOpts, CancellationToken);
        all!.Single(i => i.Id == itemId).TypicalSeconds.Should().Be(25 * 60);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// A rolling period whose current cycle opened at <see cref="LastReset"/>, with <paramref name="closedCycles"/>
    /// ledger rows behind it — the reset instants the cycles are cut at.
    /// </summary>
    private async Task<(long PeriodId, int LengthInDays)> SeedWeeklyPeriodAsync(int closedCycles, int lengthInDays = 7)
    {
        await using var db = CreateDbContext();
        var periodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(
            db, lengthInDays: lengthInDays, resetAnchorDay: 0, ct: CancellationToken);

        var period = await db.Set<RoutineTimePeriod>().SingleAsync(p => p.Id == periodId, CancellationToken);
        period.LastResetAt = LastReset;
        await db.SaveChangesAsync(CancellationToken);

        var end = DateOnly.FromDateTime(LastReset);
        for (var k = 0; k < closedCycles; k++)
        {
            await TodoListTestSeedHelper.SeedRoutinePeriodCompletionAsync(
                db, periodId, end.AddDays(-lengthInDays * (k + 1)), end.AddDays(-lengthInDays * k), ct: CancellationToken);
        }

        return (periodId, lengthInDays);
    }

    private async Task<(long ItemId, long ActivityId)> SeedItemAsync(long periodId, string name, bool isDone = false)
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"typical-{name}", UserId, CancellationToken);
        var itemId = await TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, activityId, periodId, isDone: isDone, ct: CancellationToken);
        return (itemId, activityId);
    }

    private async Task LogAsync(long activityId, long? routineTodoListId, DateTime startUtc, int seconds)
    {
        await using var db = CreateDbContext();
        db.Set<ActivityHistory>().Add(new ActivityHistory
        {
            UserId = UserId,
            ActivityId = activityId,
            StartTimestamp = startUtc,
            EndTimestamp = startUtc.AddSeconds(seconds),
            Length = new IntTime(seconds),
            RoutineTodoListId = routineTodoListId
        });
        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task<List<RoutineTodoListGroupedResponse>> GroupedAsync()
    {
        var response = await CreateClient().GetAsync("api/routine-todo-list/grouped-by-time-period", CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return (await response.Content.ReadFromJsonAsync<List<RoutineTodoListGroupedResponse>>(JsonOpts, CancellationToken))!;
    }

    private async Task<RoutineTodoListResponse> GroupedItemAsync(long itemId) =>
        (await GroupedAsync()).SelectMany(g => g.Items).Single(i => i.Id == itemId);
}
