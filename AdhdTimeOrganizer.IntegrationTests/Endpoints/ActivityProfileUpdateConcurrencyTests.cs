using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.infrastructure.persistence;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-7 — the three profile <c>PUT</c>s: the happy path, and what a lost update answers.
///
/// <para>Two silent failures. The first is the status mapping: <c>BaseEntityConfigure()</c> puts a live
/// <c>row_version</c> token on all three profiles, so an overlapping load-then-save genuinely throws
/// <see cref="DbUpdateConcurrencyException"/> — but a hand-written <c>catch</c> that ends in a literal
/// <c>Send.ErrorsAsync(400, ct)</c> reports that lost update as a client input error, and the client
/// retries a request that can never succeed. Only the status is wrong, so nothing logs and nothing throws.
/// The second is plainer: until this suite there was no test anywhere that a profile update *succeeds* and
/// writes the new column values.</para>
///
/// <para>A race cannot be asserted strictly without flaking, so the strict pin here is
/// <see cref="LostUpdate_MapsToA409_NotA400"/> — a real conflict, forced deterministically at the
/// DbContext level, run through the exact <c>DbUtils.HandleException</c> → <c>EndpointHelper.ToStatusCode</c>
/// pair the endpoints' catch block uses. The HTTP race beside it carries the weak "never 400" assertion.</para>
/// </summary>
[Collection("Postgres")]
public class ActivityProfileUpdateConcurrencyTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string Backlog = "backlog";
    private const string BucketList = "bucketList";
    private const string Project = "project";

    /// <summary>
    /// Seeds one profile of the given kind for the caller and builds a full, valid update payload for it
    /// with every mapped column changed. The lookup ids are read back off the seeded row: the update
    /// validators reject a lookup id that is not the caller's, so the payload has to reuse the caller's own.
    /// </summary>
    private static async Task<(string route, object payload)> SeedAndBuildUpdateAsync(DbContext db, string family, string name)
    {
        switch (family)
        {
            case Backlog:
            {
                var (_, profileId) = await ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db, FakeLoggedUserService.TestUserId, name);
                var seeded = await db.Set<ActivityBacklogProfile>().AsNoTracking().FirstAsync(p => p.Id == profileId, CancellationToken);
                return ($"api/activity-backlog-profile/{profileId}", new
                {
                    ActivityId = seeded.ActivityId,
                    LocationTypeId = seeded.LocationTypeId,
                    WeatherDependencyId = seeded.WeatherDependencyId,
                    ExpectedCostTierId = seeded.ExpectedCostTierId,
                    EnergyLevel = "High",
                    EffortType = "Mental",
                    MinParticipants = 2,
                    MaxParticipants = 4,
                    DurationMinutes = 90,
                    IsRepeatable = true
                });
            }
            case BucketList:
            {
                var (_, profileId) = await ActivityProfileTestSeedHelper.SeedBucketListProfileAsync(db, FakeLoggedUserService.TestUserId, name);
                var seeded = await db.Set<ActivityBucketListProfile>().AsNoTracking().FirstAsync(p => p.Id == profileId, CancellationToken);
                return ($"api/activity-bucket-list-profile/{profileId}", new
                {
                    ActivityId = seeded.ActivityId,
                    ExperienceTypeId = seeded.ExperienceTypeId,
                    ComfortZoneStep = 2,
                    RequiresTravel = true,
                    FinancialGoal = 1500m,
                    InspirationSource = "A documentary"
                });
            }
            default:
            {
                var (_, profileId) = await ActivityProfileTestSeedHelper.SeedProjectProfileAsync(db, FakeLoggedUserService.TestUserId, name);
                var seeded = await db.Set<ActivityProjectProfile>().AsNoTracking().FirstAsync(p => p.Id == profileId, CancellationToken);
                return ($"api/activity-project-profile/{profileId}", new
                {
                    ActivityId = seeded.ActivityId,
                    DifficultyLevel = "Expert",
                    ProjectArea = "Metalworking",
                    EstimatedHours = 12m,
                    IsMessy = true,
                    MaterialsNeeded = new[] { "Steel sheet" },
                    RequiredTools = new[] { "Angle grinder" },
                    ReadinessStatus = "ReadyToStart"
                });
            }
        }
    }

    private static long IdFromRoute(string route) => long.Parse(route[(route.LastIndexOf('/') + 1)..]);

    private static async Task AssertUpdateLandedAsync(DbContext db, string family, long profileId)
    {
        switch (family)
        {
            case Backlog:
                var backlog = await db.Set<ActivityBacklogProfile>().AsNoTracking().FirstAsync(p => p.Id == profileId, CancellationToken);
                backlog.EnergyLevel.Should().Be(EnergyLevel.High);
                backlog.EffortType.Should().Be(EffortType.Mental);
                backlog.MinParticipants.Should().Be(2);
                backlog.MaxParticipants.Should().Be(4);
                backlog.DurationMinutes.Should().Be(90);
                backlog.IsRepeatable.Should().BeTrue();
                break;
            case BucketList:
                var bucket = await db.Set<ActivityBucketListProfile>().AsNoTracking().FirstAsync(p => p.Id == profileId, CancellationToken);
                bucket.ComfortZoneStep.Should().Be(2);
                bucket.RequiresTravel.Should().BeTrue();
                bucket.FinancialGoal.Should().Be(1500m);
                bucket.InspirationSource.Should().Be("A documentary");
                break;
            default:
                var project = await db.Set<ActivityProjectProfile>().AsNoTracking().FirstAsync(p => p.Id == profileId, CancellationToken);
                project.DifficultyLevel.Should().Be(DifficultyLevel.Expert);
                project.ProjectArea.Should().Be("Metalworking");
                project.EstimatedHours.Should().Be(12m);
                project.IsMessy.Should().BeTrue();
                project.MaterialsNeeded.Should().ContainSingle().Which.Should().Be("Steel sheet");
                project.RequiredTools.Should().ContainSingle().Which.Should().Be("Angle grinder");
                project.ReadinessStatus.Should().Be(ReadinessStatus.ReadyToStart);
                break;
        }
    }

    // ---- 3: the happy path, which nothing asserted before ------------------------------------------

    [Theory(DisplayName = "PUT on a profile succeeds and writes every mapped column")]
    [InlineData(Backlog)]
    [InlineData(BucketList)]
    [InlineData(Project)]
    public async Task Update_WithAValidPayload_WritesTheNewValues(string family)
    {
        string route;
        object payload;
        await using (var db = CreateDbContext())
            (route, payload) = await SeedAndBuildUpdateAsync(db, family, $"{family} happy path");

        var response = await CreateClient().PutAsJsonAsync(route, payload, JsonOpts, CancellationToken);

        // 200 with the id in the body, not 204: these three now run on BaseUpdateEndpoint, which ends in
        // Send.OkAsync(entity.Id).
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var verifyDb = CreateDbContext();
        await AssertUpdateLandedAsync(verifyDb, family, IdFromRoute(route));
    }

    // ---- 1: the race, with the weak assertion ------------------------------------------------------

    [Theory(DisplayName = "RACE (weak assertion): two overlapping PUTs to one profile never answer 400 -- a lost update is a 409")]
    [InlineData(Backlog)]
    [InlineData(BucketList)]
    [InlineData(Project)]
    public async Task TwoConcurrentUpdates_NeverAnswer400(string family)
    {
        string route;
        object payload;
        await using (var db = CreateDbContext())
            (route, payload) = await SeedAndBuildUpdateAsync(db, family, $"{family} race");

        // Whether the two requests actually overlap is up to the scheduler, so this asserts what must hold
        // either way rather than a fixed pair of codes: the serialized outcome is two 200s, the overlapping
        // one is a 200 and a 409, and a 400 is wrong in both.
        var clientOne = CreateClient();
        var clientTwo = CreateClient();
        var responses = await Task.WhenAll(
            clientOne.PutAsJsonAsync(route, payload, JsonOpts, CancellationToken),
            clientTwo.PutAsJsonAsync(route, payload, JsonOpts, CancellationToken));

        responses.Should().Contain(r => r.StatusCode == HttpStatusCode.OK, "at least one of the two writers must win");
        responses.Where(r => r.StatusCode != HttpStatusCode.OK)
            .Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict,
                "a lost update is the only way the loser can fail here, and that is a 409");

        await using var verifyDb = CreateDbContext();
        await AssertUpdateLandedAsync(verifyDb, family, IdFromRoute(route));
    }

    // ---- 2: the deterministic half -----------------------------------------------------------------

    [Fact(DisplayName = "A real lost update maps to 409 through DbUtils/EndpointHelper -- the pair the PUT's catch block uses")]
    public async Task LostUpdate_MapsToA409_NotA400()
    {
        long profileId;
        await using (var seedDb = CreateDbContext())
            (_, profileId) = await ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db: seedDb, userId: FakeLoggedUserService.TestUserId, name: "Lost update");

        // Two overlapping loads, two saves -- the in-flight conflict BaseUpdateEndpoint's class comment
        // describes, forced by hand so there is no race to flake on.
        await using var contextA = CreateDbContext();
        var trackedByA = await contextA.Set<ActivityBacklogProfile>().FirstAsync(p => p.Id == profileId, CancellationToken);

        await using (var contextB = CreateDbContext())
        {
            var trackedByB = await contextB.Set<ActivityBacklogProfile>().FirstAsync(p => p.Id == profileId, CancellationToken);
            trackedByB.DurationMinutes = 45;
            await contextB.SaveChangesAsync(CancellationToken);   // row_version moves
        }

        trackedByA.DurationMinutes = 60;
        var act = async () => await contextA.SaveChangesAsync(CancellationToken);

        var thrown = (await act.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "BaseEntityConfigure() puts a live row_version token on the profile")).Which;

        // The catch block in every framework command base is exactly this pair. A hand-written endpoint that
        // hard-codes 400 instead reports this same exception as a client input error.
        var result = DbUtils.HandleException(thrown, "HandleAsync");
        EndpointHelper.ToStatusCode(result.ErrorType).Should().Be(409);

        // B's write is the one that survived; A's was refused, not silently applied.
        await using var verifyDb = CreateDbContext();
        var row = await verifyDb.Set<ActivityBacklogProfile>().AsNoTracking().FirstAsync(p => p.Id == profileId, CancellationToken);
        row.DurationMinutes.Should().Be(45);
    }

    /// <summary>
    /// The FK-violation route the TEST-7 prompt suggested as the deterministic half is unreachable over HTTP
    /// now: the update validators check lookup ownership before the handler runs, and answer the same
    /// "Lookup not found." for a foreign id as for one that does not exist (deliberately — see
    /// <c>LookupOwnership</c>, which keeps the endpoint from being an existence oracle). So this 400 is the
    /// validator's, and it is correct; the catch block's mapping is pinned by
    /// <see cref="LostUpdate_MapsToA409_NotA400"/> instead.
    /// </summary>
    [Fact(DisplayName = "PUT with a lookup id that does not exist is the validator's 400, before the handler runs")]
    public async Task Update_WithAnUnknownLookupId_Is400FromTheValidator()
    {
        long profileId;
        long activityId;
        await using (var db = CreateDbContext())
            (activityId, profileId) = await ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db, FakeLoggedUserService.TestUserId, "Unknown lookup");

        var payload = new
        {
            ActivityId = activityId,
            LocationTypeId = long.MaxValue,
            WeatherDependencyId = long.MaxValue,
            ExpectedCostTierId = long.MaxValue,
            EnergyLevel = "High",
            MinParticipants = 1,
            DurationMinutes = 30,
            IsRepeatable = false
        };

        var response = await CreateClient().PutAsJsonAsync($"api/activity-backlog-profile/{profileId}", payload, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var verifyDb = CreateDbContext();
        var row = await verifyDb.Set<ActivityBacklogProfile>().AsNoTracking().FirstAsync(p => p.Id == profileId, CancellationToken);
        row.DurationMinutes.Should().Be(30, "the rejected update must not have touched the row");
        row.LocationTypeId.Should().NotBe(long.MaxValue);
    }
}
