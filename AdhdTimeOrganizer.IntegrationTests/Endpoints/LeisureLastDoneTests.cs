using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.Testing;
using Xunit;
using static AdhdTimeOrganizer.IntegrationTests.Endpoints.ActivityProfileTestSeedHelper;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// F3-06 — the leisure draw learns what the user actually did, through Core's <c>IActivityLastDoneSource</c>
/// seam: History's recordings, Planning's completed (and untouched) blocks, and ActivityProfiles' memory anchors.
/// </summary>
/// <remarks>
/// Every source resolves by string key and a missing one is silent: the draw falls back to "last shown" and still
/// returns plausible cards. So each source is asserted on the <c>lastDoneAt</c> it puts on the card, and the
/// unkept-commitment demotion on how often a demoted row wins across seeds — a single draw is dominated by its
/// jitter, and the demotion is deliberately smaller than it.
/// </remarks>
[Collection("Postgres")]
public class LeisureLastDoneTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private const string DrawRoute = "/api/leisure-suggestion";

    // ─── each source reaches the card ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ARecording_IsWhenItWasLastDone()
    {
        await using var db = CreateDbContext();
        var (activityId, _) = await SeedBacklogProfileAsync(db, UserId, "Evening walk");
        var started = TruncatedToSecond(DateTime.UtcNow.AddDays(-3));
        await SeedRecordingAsync(db, activityId, started);

        var item = await DrawItemAsync(activityId);

        item.LastDoneAt.Should().Be(started);
    }

    [Fact]
    public async Task ACompletedBlock_IsWhenItWasLastDone_AndAnUntouchedOneIsNot()
    {
        await using var db = CreateDbContext();
        var (activityId, _) = await SeedBacklogProfileAsync(db, UserId, "Board games");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await SeedBlockAsync(db, activityId, today.AddDays(-4), new TimeOnly(19, 0), PlannerTaskStatus.Completed);
        await SeedBlockAsync(db, activityId, today.AddDays(-2), new TimeOnly(19, 0), PlannerTaskStatus.NotStarted);

        var item = await DrawItemAsync(activityId);

        // The test user is on UTC, so the block's wall clock is the instant.
        item.LastDoneAt.Should().Be(today.AddDays(-4).ToDateTime(new TimeOnly(19, 0), DateTimeKind.Utc),
            "an untouched block is not evidence of anything — only positive evidence moves this");
    }

    [Fact]
    public async Task AMemoryAnchor_IsTheFirstOfItsMonth()
    {
        await using var db = CreateDbContext();
        var (activityId, _) = await SeedBacklogProfileAsync(db, UserId, "Stargazing");
        var lastMonth = DateTime.UtcNow.AddMonths(-1);
        await SeedMemoryAnchorAsync(db, UserId, activityId, lastMonth.Year, lastMonth.Month);

        var item = await DrawItemAsync(activityId);

        item.LastDoneAt.Should().Be(new DateTime(lastMonth.Year, lastMonth.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            "the anchor proves the month and nothing more precise, so it never claims more recency than that");
    }

    [Fact]
    public async Task TheLatestEvidenceWins_AcrossSources()
    {
        await using var db = CreateDbContext();
        var (activityId, _) = await SeedBacklogProfileAsync(db, UserId, "Swimming");
        var lastMonth = DateTime.UtcNow.AddMonths(-1);
        await SeedMemoryAnchorAsync(db, UserId, activityId, lastMonth.Year, lastMonth.Month);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await SeedBlockAsync(db, activityId, today.AddDays(-6), new TimeOnly(18, 0), PlannerTaskStatus.Completed);
        var recorded = TruncatedToSecond(DateTime.UtcNow.AddDays(-2));
        await SeedRecordingAsync(db, activityId, recorded);

        var item = await DrawItemAsync(activityId);

        item.LastDoneAt.Should().Be(recorded);
    }

    [Fact]
    public async Task NoEvidence_SaysNothing()
    {
        await using var db = CreateDbContext();
        var (activityId, _) = await SeedBacklogProfileAsync(db, UserId, "Pottery");

        (await DrawItemAsync(activityId)).LastDoneAt.Should().BeNull("a card says nothing rather than guess");
    }

    [Fact]
    public async Task AnotherUsersRecording_IsNotEvidence()
    {
        var otherUserId = await CreateSecondUserAsync(Fixture, "lastdone-b@test.com");
        await using var db = CreateDbContext();
        var (activityId, _) = await SeedBacklogProfileAsync(db, UserId, "Chess");
        // A row on the caller's activity id but stamped with someone else's user id: only the user predicate
        // stands between it and the caller's card.
        db.Set<ActivityHistory>().Add(new ActivityHistory
        {
            UserId = otherUserId,
            ActivityId = activityId,
            StartTimestamp = DateTime.UtcNow.AddDays(-1),
            EndTimestamp = DateTime.UtcNow.AddDays(-1).AddMinutes(30),
            Length = new IntTime(0, 30)
        });
        await db.SaveChangesAsync(CancellationToken);

        (await DrawItemAsync(activityId)).LastDoneAt.Should().BeNull();
    }

    // ─── the ordering it changes ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SomethingDoneYesterday_IsNotOfferedFirstTonight()
    {
        await using var db = CreateDbContext();
        var (doneId, _) = await SeedBacklogProfileAsync(db, UserId, "Yoga");
        var (otherId, _) = await SeedBacklogProfileAsync(db, UserId, "Juggling");
        await SeedRecordingAsync(db, doneId, DateTime.UtcNow.AddDays(-1));

        var first = await CountSeedsWhereFirstOutranksAsync(doneId, otherId);

        first.Should().Be(0, "done yesterday is buried by more than the jitter can bridge");
    }

    [Fact]
    public async Task TwoUntouchedPlans_DemoteSoftly_AndAreForgivenOnceItIsDone()
    {
        await using var db = CreateDbContext();
        var (gamesId, _) = await SeedBacklogProfileAsync(db, UserId, "Board games night");
        var (controlId, _) = await SeedBacklogProfileAsync(db, UserId, "Sketching");
        var baseline = await CountSeedsWhereFirstOutranksAsync(gamesId, controlId);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await SeedBlockAsync(db, gamesId, today.AddDays(-12), new TimeOnly(19, 0), PlannerTaskStatus.NotStarted);
        await SeedBlockAsync(db, gamesId, today.AddDays(-10), new TimeOnly(19, 0), PlannerTaskStatus.NotStarted);
        var demoted = await CountSeedsWhereFirstOutranksAsync(gamesId, controlId);

        demoted.Should().BeLessThan(baseline, "two planned evenings passed untouched");
        demoted.Should().BeGreaterThan(0, "soft: a twice-missed favourite still comes up some evenings");

        // Done eight days ago — after both misses, and far enough back that staleness has saturated — so the
        // misses no longer count and nothing else about the row changed.
        await SeedRecordingAsync(db, gamesId, DateTime.UtcNow.AddDays(-8));
        var forgiven = await CountSeedsWhereFirstOutranksAsync(gamesId, controlId);

        forgiven.Should().Be(baseline, "a miss the user later made up for is forgotten");
    }

    [Fact]
    public async Task OneUntouchedPlan_ChangesNothing()
    {
        await using var db = CreateDbContext();
        var (gamesId, _) = await SeedBacklogProfileAsync(db, UserId, "Puzzle");
        var (controlId, _) = await SeedBacklogProfileAsync(db, UserId, "Knitting");
        var baseline = await CountSeedsWhereFirstOutranksAsync(gamesId, controlId);

        await SeedBlockAsync(db, gamesId, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3), new TimeOnly(19, 0),
            PlannerTaskStatus.NotStarted);

        (await CountSeedsWhereFirstOutranksAsync(gamesId, controlId)).Should().Be(baseline,
            "one evening that went differently is not a pattern");
    }

    // ─── helpers ────────────────────────────────────────────────────────────────────────────────────

    private record ActivityInfo(long Id, string Name);

    private record Item(string Key, long ActivityId, ActivityInfo Activity, DateTime? LastDoneAt);

    private record DrawResponse(List<Item> Items);

    private async Task<DrawResponse> DrawAsync(long seed = 1)
    {
        var response = await CreateClient().PostAsJsonAsync(DrawRoute,
            new { Minutes = 240, Energy = "low", People = 1, Seed = seed, Count = 3 }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<DrawResponse>(JsonOpts, CancellationToken))!;
    }

    private async Task<Item> DrawItemAsync(long activityId) =>
        (await DrawAsync()).Items.Should().Contain(i => i.ActivityId == activityId).Which;

    private async Task<int> CountSeedsWhereFirstOutranksAsync(long firstId, long secondId)
    {
        var count = 0;
        for (var seed = 1; seed <= 40; seed++)
        {
            var ids = (await DrawAsync(seed)).Items.Select(i => i.ActivityId).ToList();
            if (ids.IndexOf(firstId) < ids.IndexOf(secondId))
                count++;
        }

        return count;
    }

    private static DateTime TruncatedToSecond(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);

    private static async Task SeedRecordingAsync(DbContext db, long activityId, DateTime startUtc)
    {
        db.Set<ActivityHistory>().Add(new ActivityHistory
        {
            UserId = UserId,
            ActivityId = activityId,
            StartTimestamp = startUtc,
            EndTimestamp = startUtc.AddMinutes(30),
            Length = new IntTime(0, 30)
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task SeedBlockAsync(DbContext db, long activityId, DateOnly date, TimeOnly start, PlannerTaskStatus status)
    {
        var ct = TestContext.Current.CancellationToken;
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, date, UserId, ct);
        await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, start, start.AddHours(1), UserId, status, ct: ct);
    }
}
