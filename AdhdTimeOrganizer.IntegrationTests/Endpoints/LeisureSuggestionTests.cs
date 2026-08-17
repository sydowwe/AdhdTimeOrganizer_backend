using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.@base;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// The leisure draw over HTTP — <c>POST /leisure-suggestion</c> and <c>POST /leisure-suggestion/seen</c>.
///
/// <para>The rule itself is covered without a database in <c>LeisureDrawRankerTests</c>. What these tests pin is
/// everything around it that fails silently: that the draw only ever sees the caller's own profiles (the
/// <c>Activity*Profile</c> entities carry no query filter, so the hand-scoping is the whole guard), that the
/// suggestion history is actually read back and actually changes the next draw, and that recording an outcome is
/// an upsert rather than an append. All of it asserted on rows and on which cards come back, never on a route
/// answering 200.</para>
/// </summary>
[Collection("Postgres")]
public class LeisureSuggestionTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private const string DrawRoute = "/api/leisure-suggestion";
    private const string SeenRoute = "/api/leisure-suggestion/seen";
    private const string Password = "Test@1234!";

    // ─── the shape of a card ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ABacklogCard_CarriesTheFactsItRenders()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedBacklogAsync(db, UserId, "Evening walk", duration: 45,
            energy: EnergyLevel.Low, effort: EffortType.Physical, locationText: "Outdoor");

        var draw = await DrawAsync(minutes: 45, energy: "low");

        var item = draw.Items.Should().ContainSingle().Subject;
        item.Key.Should().Be($"backlog:{activityId}");
        item.Source.Should().Be("backlog");
        item.ActivityId.Should().Be(activityId);
        item.Activity.Name.Should().Be("Evening walk");
        item.StatedDurationMinutes.Should().Be(45, "the backlog states its duration, so the card says \"takes 45 min\"");
        item.MaxUsefulMinutes.Should().Be(45);
        item.EnergyLevel.Should().Be("low");
        item.EnergyIsDerived.Should().BeFalse("the backlog states an energy level rather than implying one");
        item.EffortType.Should().Be("physical");
        item.MinParticipants.Should().Be(1);
        item.ReadinessStatus.Should().BeNull();
        item.ComfortZoneStep.Should().BeNull();
        item.RequiresTravel.Should().BeFalse();
        item.ContextLabel.Should().Be("Outdoor");
    }

    [Fact]
    public async Task AProjectCard_SaysItsEnergyWasDerived_AndSizesFromTheWholeEstimate()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedProjectAsync(db, UserId, "Build a shelf", DifficultyLevel.Expert,
            ReadinessStatus.ReadyToStart, estimatedHours: 2.5m, area: "Woodworking");

        var draw = await DrawAsync(minutes: 120, energy: "high");

        var item = draw.Items.Should().ContainSingle().Subject;
        item.Key.Should().Be($"project:{activityId}");
        item.Source.Should().Be("project");
        item.StatedDurationMinutes.Should().BeNull("a project's estimate is not a stated session length");
        item.MaxUsefulMinutes.Should().Be(150, "2.5 hours, in minutes");
        item.EnergyLevel.Should().Be("high", "derived from Expert difficulty");
        item.EnergyIsDerived.Should().BeTrue();
        item.ReadinessStatus.Should().Be("readyToStart");
        item.ContextLabel.Should().Be("Woodworking");
    }

    [Fact]
    public async Task ABucketListCard_CarriesItsStepAndTravelFlag()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedBucketListAsync(db, UserId, "See the northern lights",
            comfortZoneStep: 4, requiresTravel: true, experienceText: "Travel");

        var draw = await DrawAsync(minutes: 480, energy: "high");

        var item = draw.Items.Should().ContainSingle().Subject;
        item.Key.Should().Be($"bucketList:{activityId}");
        item.Source.Should().Be("bucketList");
        item.MaxUsefulMinutes.Should().BeNull("these rows record no duration at all");
        item.EnergyIsDerived.Should().BeTrue();
        item.ComfortZoneStep.Should().Be(4);
        item.RequiresTravel.Should().BeTrue();
        item.ContextLabel.Should().Be("Travel");
    }

    // ─── the two counts the empty state is built from ────────────────────────────────────────────────

    [Fact]
    public async Task WithNothingFiled_ThePoolIsEmpty()
    {
        var draw = await DrawAsync(minutes: 240, energy: "low");

        draw.PoolCount.Should().Be(0, "which is what renders \"you have nothing filed yet\"");
        draw.EligibleCount.Should().Be(0);
        draw.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task WithSomethingFiledButNothingMatching_ThePoolIsNotEmpty()
    {
        await using var db = CreateDbContext();
        await SeedBacklogAsync(db, UserId, "Long hike", duration: 300);
        await SeedProjectAsync(db, UserId, "Big build", estimatedHours: 20);
        await SeedBucketListAsync(db, UserId, "Skydive", requiresTravel: true);

        var draw = await DrawAsync(minutes: 20, energy: "low");

        draw.PoolCount.Should().Be(3, "the count is taken before every constraint, including the source floors");
        draw.EligibleCount.Should().Be(0);
        draw.Items.Should().BeEmpty("which is what renders \"nothing matches, try loosening something\"");
    }

    [Fact]
    public async Task AProjectThatNeedsShopping_IsNeverDrawn()
    {
        await using var db = CreateDbContext();
        await SeedProjectAsync(db, UserId, "Needs paint", readiness: ReadinessStatus.NeedsShopping);

        var draw = await DrawAsync(minutes: 240, energy: "low");

        draw.PoolCount.Should().Be(1);
        draw.EligibleCount.Should().Be(0);
        draw.Items.Should().BeEmpty("an errand is not a suggestion");
    }

    // ─── the seed contract ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReloadingTheSameSeededDraw_ShowsTheSameCardsInTheSameOrder()
    {
        await using var db = CreateDbContext();
        for (var i = 0; i < 8; i++)
            await SeedBacklogAsync(db, UserId, $"Backlog {i}", duration: 30);

        var first = await DrawAsync(minutes: 60, energy: "low", seed: 1837465239);
        var second = await DrawAsync(minutes: 60, energy: "low", seed: 1837465239);

        first.Items.Should().HaveCount(3);
        second.Items.Select(i => i.Key).Should().Equal(first.Items.Select(i => i.Key),
            "the seeded URL has to reproduce the three cards the user was deciding between");
    }

    // ─── the memory: what a draw remembers ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RejectingADraw_ChangesWhatTheNextOneOffers_AtTheSameSeed()
    {
        // The whole reason this endpoint exists: the record is server-side, so this holds across devices.
        await using var db = CreateDbContext();
        for (var i = 0; i < 6; i++)
            await SeedBacklogAsync(db, UserId, $"Backlog {i}", duration: 30);

        var first = await DrawAsync(minutes: 60, energy: "low", seed: 99);
        var rejected = first.Items.Select(i => i.Key).ToList();
        rejected.Should().HaveCount(3);

        (await SeenAsync(rejected, "rejected")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var second = await DrawAsync(minutes: 60, energy: "low", seed: 99);

        second.Items.Should().HaveCount(3);
        second.Items.Select(i => i.Key).Should().NotIntersectWith(rejected,
            "shown-today is buried by more than the jitter can bridge, so a reroll moves on");
    }

    [Fact]
    public async Task RenderingADraw_RecordsNothingByItself()
    {
        await using var db = CreateDbContext();
        await SeedBacklogAsync(db, UserId, "Read a chapter", duration: 30);

        await DrawAsync(minutes: 60, energy: "low");

        await using var assertDb = CreateDbContext();
        var rows = await assertDb.Set<LeisureSuggestionRecord>().IgnoreQueryFilters().CountAsync(CancellationToken);
        rows.Should().Be(0,
            "recording on render would demote the very cards on screen and stop a seeded URL reproducing its own draw");
    }

    [Fact]
    public async Task RecordingTheSameKeyTwice_MovesTheRowRatherThanAddingOne()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedBacklogAsync(db, UserId, "Play guitar", duration: 30);
        var key = $"backlog:{activityId}";

        (await SeenAsync([key], "rejected")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await SeenAsync([key], "committed")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var assertDb = CreateDbContext();
        var records = await assertDb.Set<LeisureSuggestionRecord>()
            .IgnoreQueryFilters()
            .Where(r => r.UserId == UserId)
            .ToListAsync(CancellationToken);

        var record = records.Should().ContainSingle("(user, source, activity) is unique — a second reroll is an update").Subject;
        record.ActivityId.Should().Be(activityId);
        record.Source.Should().Be(LeisureSuggestionSource.Backlog);
        record.LastOutcome.Should().Be(LeisureSuggestionOutcome.Committed);
    }

    [Fact]
    public async Task TheSameActivityInTwoSources_KeepsTwoIndependentHistories()
    {
        // One activity can be both a backlog entry and a project; those are two suggestions, and burying one
        // must not bury the other.
        await using var db = CreateDbContext();
        var activityId = await SeedBacklogAsync(db, UserId, "Restore the bike", duration: 45);
        db.Set<ActivityProjectProfile>().Add(new ActivityProjectProfile
        {
            ActivityId = activityId,
            DifficultyLevel = DifficultyLevel.Intermediate,
            ProjectArea = "Garage",
            EstimatedHours = 6,
            ReadinessStatus = ReadinessStatus.ReadyToStart
        });
        await db.SaveChangesAsync(CancellationToken);

        (await SeenAsync([$"backlog:{activityId}"], "rejected")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var draw = await DrawAsync(minutes: 120, energy: "medium");

        draw.PoolCount.Should().Be(2);
        // Both are drawn — there are only two cards to fill three slots — but the rejected side is the one that
        // ranks behind, and it is the only side the record buried.
        draw.Items.Select(i => i.Key).Should().Equal($"project:{activityId}", $"backlog:{activityId}");

        await using var assertDb = CreateDbContext();
        var records = await assertDb.Set<LeisureSuggestionRecord>()
            .IgnoreQueryFilters()
            .Where(r => r.ActivityId == activityId)
            .Select(r => r.Source)
            .ToListAsync(CancellationToken);

        records.Should().Equal([LeisureSuggestionSource.Backlog], "only the backlog side was rejected");
    }

    /// <summary>
    /// The variety signal reaches the ranking, which is the seam most likely to fail in silence: a
    /// <c>lastCommittedEffort</c> read that always came back null would still return three perfectly plausible
    /// cards. Swept across seeds rather than spot-checked, because a single draw is dominated by its jitter — and
    /// the committed activity is a third row, so the effect measured here is variety and not staleness.
    /// </summary>
    [Fact]
    public async Task CommittingToSomethingPhysical_TiltsTheNextDrawTowardsSomethingMental()
    {
        await using var db = CreateDbContext();
        var committedId = await SeedBacklogAsync(db, UserId, "Committed run", duration: 30, effort: EffortType.Physical);
        var physicalId = await SeedBacklogAsync(db, UserId, "Another run", duration: 30, effort: EffortType.Physical);
        var mentalId = await SeedBacklogAsync(db, UserId, "Crossword", duration: 30, effort: EffortType.Mental);

        var before = await CountSeedsWhereMentalComesFirstAsync(mentalId, physicalId);

        (await SeenAsync([$"backlog:{committedId}"], "committed")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await CountSeedsWhereMentalComesFirstAsync(mentalId, physicalId);

        after.Should().BeGreaterThan(before,
            "a different effort type from the last thing committed to is worth a bonus, so some draws flip");
    }

    private async Task<int> CountSeedsWhereMentalComesFirstAsync(long mentalId, long physicalId)
    {
        var count = 0;
        for (var seed = 1; seed <= 30; seed++)
        {
            var keys = (await DrawAsync(minutes: 45, energy: "low", seed: seed, count: 3)).Items
                .Select(i => i.Key)
                .ToList();
            if (keys.IndexOf($"backlog:{mentalId}") < keys.IndexOf($"backlog:{physicalId}"))
                count++;
        }

        return count;
    }

    [Fact]
    public async Task AnOldRecord_IsSweptOnTheNextWrite()
    {
        await using var db = CreateDbContext();
        var staleId = await SeedBacklogAsync(db, UserId, "Long forgotten", duration: 30);
        var freshId = await SeedBacklogAsync(db, UserId, "Just shown", duration: 30);

        db.Set<LeisureSuggestionRecord>().Add(new LeisureSuggestionRecord
        {
            UserId = UserId,
            ActivityId = staleId,
            Source = LeisureSuggestionSource.Backlog,
            LastSuggestedAt = DateTime.UtcNow.AddDays(-120),
            LastOutcome = LeisureSuggestionOutcome.Rejected
        });
        await db.SaveChangesAsync(CancellationToken);

        (await SeenAsync([$"backlog:{freshId}"], "rejected")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var assertDb = CreateDbContext();
        var remaining = await assertDb.Set<LeisureSuggestionRecord>()
            .IgnoreQueryFilters()
            .Where(r => r.UserId == UserId)
            .Select(r => r.ActivityId)
            .ToListAsync(CancellationToken);

        remaining.Should().Equal([freshId], "past 90 days the record has stopped meaning anything");
    }

    [Fact]
    public async Task DeletingTheActivity_TakesItsDrawHistoryWithIt()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedBacklogAsync(db, UserId, "Doomed activity", duration: 30);
        await SeenAsync([$"backlog:{activityId}"], "rejected");

        (await CreateClient().DeleteAsync($"api/activity/{activityId}", CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var assertDb = CreateDbContext();
        var rows = await assertDb.Set<LeisureSuggestionRecord>()
            .IgnoreQueryFilters()
            .CountAsync(r => r.ActivityId == activityId, CancellationToken);

        rows.Should().Be(0, "a record that outlived its activity would keep a key alive no draw can produce");
    }

    // ─── constraints the database applies ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ACostCeiling_ExcludesEverythingMoreExpensive_ByTierOrder()
    {
        await using var db = CreateDbContext();
        var freeTierId = await SeedLookupAsync<ActivityExpectedCostTier>(db, UserId, "Free", sortOrder: 0);
        var pricyTierId = await SeedLookupAsync<ActivityExpectedCostTier>(db, UserId, "Pricy", sortOrder: 10);
        var freeActivityId = await SeedBacklogAsync(db, UserId, "Walk", duration: 30, costTierId: freeTierId);
        var pricyActivityId = await SeedBacklogAsync(db, UserId, "Climbing gym", duration: 30, costTierId: pricyTierId);

        var capped = await DrawAsync(minutes: 60, energy: "low", maxCostTierId: freeTierId);

        capped.PoolCount.Should().Be(2, "the pool is counted before the constraint");
        capped.Items.Select(i => i.ActivityId).Should().Equal([freeActivityId]);

        // A tier id that is not in the user's list any more — a deleted tier still sitting in a bookmarked URL —
        // must not silently collapse the draw to "free only".
        var unknownTier = await DrawAsync(minutes: 60, energy: "low", maxCostTierId: pricyTierId + 10_000);
        unknownTier.Items.Select(i => i.ActivityId).Should().BeEquivalentTo([freeActivityId, pricyActivityId]);
    }

    [Fact]
    public async Task ALocationConstraint_ExcludesEverywhereElse()
    {
        await using var db = CreateDbContext();
        var homeId = await SeedLookupAsync<ActivityLocationType>(db, UserId, "Home");
        var outdoorId = await SeedLookupAsync<ActivityLocationType>(db, UserId, "Outdoor");
        var homeActivityId = await SeedBacklogAsync(db, UserId, "Read", duration: 30, locationTypeId: homeId);
        await SeedBacklogAsync(db, UserId, "Walk", duration: 30, locationTypeId: outdoorId);

        var draw = await DrawAsync(minutes: 60, energy: "low", locationTypeId: homeId);

        draw.PoolCount.Should().Be(2);
        draw.Items.Select(i => i.ActivityId).Should().Equal([homeActivityId]);
    }

    // ─── ownership ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnotherUsersProfiles_AreNeverDrawn()
    {
        var otherUserId = await CreateSecondUserAsync("leisure-draw-b@test.com");
        await using var db = CreateDbContext();
        await SeedBacklogAsync(db, otherUserId, "Their evening walk", duration: 30);

        var draw = await DrawAsync(minutes: 60, energy: "low");

        draw.PoolCount.Should().Be(0,
            "Activity*Profile is not IEntityWithUser and has no query filter — the hand-scoping is the only guard");
        draw.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task AKeyNamingAnotherUsersActivity_RecordsNothing()
    {
        var otherUserId = await CreateSecondUserAsync("leisure-seen-b@test.com");
        await using var db = CreateDbContext();
        var theirActivityId = await SeedBacklogAsync(db, otherUserId, "Their activity", duration: 30);

        var response = await SeenAsync([$"backlog:{theirActivityId}"], "rejected");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "a stale or foreign key is dropped, not a 500");

        await using var assertDb = CreateDbContext();
        var rows = await assertDb.Set<LeisureSuggestionRecord>().IgnoreQueryFilters().CountAsync(CancellationToken);
        rows.Should().Be(0, "neither under the caller nor under the owner");
    }

    [Fact]
    public async Task BothRoutes_RefuseAnAnonymousCaller()
    {
        var client = CreateUnauthenticatedClient();

        (await client.PostAsJsonAsync(DrawRoute, DrawBody(45, "low"), JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync(SeenRoute, new { Keys = new[] { "backlog:1" }, Outcome = "rejected" }, JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─── validation ─────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("sleepy")]
    [InlineData("Low")]
    [InlineData("")]
    public async Task AnUnknownEnergy_IsRejectedRatherThanDefaulted(string energy)
    {
        var response = await CreateClient().PostAsJsonAsync(DrawRoute, DrawBody(45, energy), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "energy decides most of the ordering, so a typo must not silently become \"medium\"");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task AnImpossibleCount_IsRejected(int count)
    {
        var body = DrawBody(45, "low");
        var response = await CreateClient().PostAsJsonAsync(DrawRoute,
            new { body.Minutes, body.Energy, body.People, body.Seed, Count = count }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("backlog:")]
    [InlineData("Backlog:1")]
    [InlineData("wishlist:1")]
    public async Task AMalformedKey_IsRejected(string key)
    {
        var response = await SeenAsync([key], "rejected");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a key the client built by hand is a client bug");
    }

    [Fact]
    public async Task AnUnknownOutcome_IsRejected()
    {
        (await SeenAsync(["backlog:1"], "maybe")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SeenAsync([], "rejected")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ─── the commit path the card drives ────────────────────────────────────────────────────────────

    /// <summary>
    /// "Do it now" on a day that has never been planned — which is exactly what "I'm bored, give me something"
    /// looks like. The calendar row is created on the spot, and the task is stored already under way with the
    /// minute work began, both from the one POST the card makes.
    /// </summary>
    [Fact]
    public async Task DoItNow_OnADayWithNoCalendarRow_PlansTheTaskAsUnderWay()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedBacklogAsync(db, UserId, "Evening walk", duration: 45);
        var date = new DateOnly(2033, 6, 14);

        (await FindCalendarAsync(date)).Should().BeNull("the test is only meaningful on a date with no row");

        var response = await CreateClient().PostAsJsonAsync("/api/planner-task", new
        {
            StartTime = new { Hours = 19, Minutes = 5 },
            EndTime = new { Hours = 19, Minutes = 50 },
            IsBackground = false,
            ActivityId = activityId,
            Status = "InProgress",
            ActualStartTime = new { Hours = 19, Minutes = 3 },
            Date = date
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var calendar = await FindCalendarAsync(date);
        calendar.Should().NotBeNull();

        await using var assertDb = CreateDbContext();
        var task = await assertDb.Set<PlannerTask>()
            .IgnoreQueryFilters()
            .SingleAsync(t => t.CalendarId == calendar!.Id, CancellationToken);

        task.Status.Should().Be(PlannerTaskStatus.InProgress);
        task.ActualStartTime.Should().Be(new TimeOnly(19, 3),
            "the planner and the home now-bar read this column, not the status alone");
    }

    [Fact]
    public async Task AStartTimeOnATaskThatHasNotStarted_IsRejected()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedBacklogAsync(db, UserId, "Later walk", duration: 45);

        var response = await CreateClient().PostAsJsonAsync("/api/planner-task", new
        {
            StartTime = new { Hours = 19, Minutes = 0 },
            EndTime = new { Hours = 20, Minutes = 0 },
            IsBackground = false,
            ActivityId = activityId,
            Status = "NotStarted",
            ActualStartTime = new { Hours = 19, Minutes = 0 },
            Date = new DateOnly(2033, 6, 15)
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "ApplyStatus clears the actual times for NotStarted, so storing one would be a value the next status change drops");
    }

    // ─── helpers ────────────────────────────────────────────────────────────────────────────────────

    private record ActivityInfo(long Id, string Name, string? CategoryName, string? Icon, string? Color);

    private record Item(
        string Key,
        string Source,
        long ActivityId,
        ActivityInfo Activity,
        int? StatedDurationMinutes,
        int? MaxUsefulMinutes,
        string EnergyLevel,
        bool EnergyIsDerived,
        string? EffortType,
        int? MinParticipants,
        string? ReadinessStatus,
        int? ComfortZoneStep,
        bool RequiresTravel,
        string? ContextLabel);

    private record DrawResponse(List<Item> Items, int PoolCount, int EligibleCount);

    private record DrawRequestBody(int Minutes, string Energy, int People, long? MaxCostTierId, long? LocationTypeId,
        long Seed, int Count);

    private static DrawRequestBody DrawBody(int minutes, string energy, int people = 1, long? maxCostTierId = null,
        long? locationTypeId = null, long seed = 1, int count = 3) =>
        new(minutes, energy, people, maxCostTierId, locationTypeId, seed, count);

    private async Task<DrawResponse> DrawAsync(int minutes, string energy, int people = 1, long? maxCostTierId = null,
        long? locationTypeId = null, long seed = 1, int count = 3)
    {
        var response = await CreateClient().PostAsJsonAsync(DrawRoute,
            DrawBody(minutes, energy, people, maxCostTierId, locationTypeId, seed, count), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<DrawResponse>(JsonOpts, CancellationToken))!;
    }

    private Task<HttpResponseMessage> SeenAsync(IReadOnlyCollection<string> keys, string outcome) =>
        CreateClient().PostAsJsonAsync(SeenRoute, new { Keys = keys, Outcome = outcome }, JsonOpts, CancellationToken);

    private async Task<Calendar?> FindCalendarAsync(DateOnly date)
    {
        await using var db = CreateDbContext();
        return await db.Set<Calendar>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.UserId == UserId && c.Date == date, CancellationToken);
    }

    private async Task<long> CreateSecondUserAsync(string email)
    {
        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = new User
        {
            Email = email,
            UserName = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            Locale = AvailableLocales.En,
            Timezone = TimeZoneInfo.Utc
        };
        var result = await userManager.CreateAsync(user, Password);
        result.Succeeded.Should().BeTrue();
        return user.Id;
    }

    private static async Task<long> SeedLookupAsync<TLookup>(DbContext db, long userId, string text, int sortOrder = 0)
        where TLookup : BaseLookupWithUser, new()
    {
        var lookup = new TLookup { UserId = userId, Text = text, SortOrder = sortOrder };
        db.Set<TLookup>().Add(lookup);
        await db.SaveChangesAsync(CancellationToken);
        return lookup.Id;
    }

    private static async Task<long> SeedActivityAsync(DbContext db, long userId, string name)
    {
        var role = new ActivityRole { UserId = userId, Name = $"{name} role", Color = "#112233", Icon = "fas fa-star" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        var activity = new Activity { UserId = userId, Name = name, RoleId = role.Id };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);
        return activity.Id;
    }

    /// <remarks>
    /// The lookup texts default to something derived from the activity name, because they are unique per user on
    /// their text — two rows seeded as "Home" would fail on the index rather than in the assertion.
    /// </remarks>
    private static async Task<long> SeedBacklogAsync(DbContext db, long userId, string name, int duration = 30,
        EnergyLevel energy = EnergyLevel.Low, EffortType? effort = null, string? locationText = null,
        long? locationTypeId = null, long? costTierId = null)
    {
        var activityId = await SeedActivityAsync(db, userId, name);

        db.Set<ActivityBacklogProfile>().Add(new ActivityBacklogProfile
        {
            ActivityId = activityId,
            LocationTypeId = locationTypeId
                             ?? await SeedLookupAsync<ActivityLocationType>(db, userId, locationText ?? $"{name} location"),
            WeatherDependencyId = await SeedLookupAsync<ActivityWeatherDependency>(db, userId, $"{name} weather"),
            ExpectedCostTierId = costTierId ?? await SeedLookupAsync<ActivityExpectedCostTier>(db, userId, $"{name} cost"),
            EnergyLevel = energy,
            EffortType = effort,
            MinParticipants = 1,
            DurationMinutes = duration
        });
        await db.SaveChangesAsync(CancellationToken);
        return activityId;
    }

    private static async Task<long> SeedProjectAsync(DbContext db, long userId, string name,
        DifficultyLevel difficulty = DifficultyLevel.Beginner, ReadinessStatus readiness = ReadinessStatus.ReadyToStart,
        decimal estimatedHours = 4, string area = "Garage")
    {
        var activityId = await SeedActivityAsync(db, userId, name);

        db.Set<ActivityProjectProfile>().Add(new ActivityProjectProfile
        {
            ActivityId = activityId,
            DifficultyLevel = difficulty,
            ProjectArea = area,
            EstimatedHours = estimatedHours,
            ReadinessStatus = readiness
        });
        await db.SaveChangesAsync(CancellationToken);
        return activityId;
    }

    private static async Task<long> SeedBucketListAsync(DbContext db, long userId, string name,
        int comfortZoneStep = 2, bool requiresTravel = false, string? experienceText = null)
    {
        var activityId = await SeedActivityAsync(db, userId, name);

        db.Set<ActivityBucketListProfile>().Add(new ActivityBucketListProfile
        {
            ActivityId = activityId,
            ExperienceTypeId = await SeedLookupAsync<ActivityExperienceType>(db, userId,
                experienceText ?? $"{name} experience"),
            ComfortZoneStep = comfortZoneStep,
            RequiresTravel = requiresTravel,
            InspirationSource = "A friend's story"
        });
        await db.SaveChangesAsync(CancellationToken);
        return activityId;
    }
}
