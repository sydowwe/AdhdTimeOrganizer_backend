using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-5 — the four <c>Activity*</c> lookup ids a profile write copies straight out of the request
/// body, pinned against SEC-2.
///
/// <para>What fails silently: the lookups are <c>BaseLookupWithUser</c> — every account has its own
/// set — but a profile stores them as raw <c>long</c> FK columns and the profiles themselves are not
/// <c>IEntityWithUser</c>. The FK proves the row exists, never that it is the caller's, so before
/// <c>LookupOwnership</c> a create or update naming a stranger's sequential id inserted cleanly with a
/// 201/200. Nothing in the request pipeline, the query filters or the schema objected. Only the
/// validators do, which is why every scenario here asserts the <b>row</b> as well as the status: delete
/// the three <c>Any</c>s out of either validator and a status-code-only test still passes on the
/// happy-path cases and gets a 400 from the range rules on some of the rest.</para>
///
/// <para>Both halves of each write surface are covered on purpose. The update validators are separate
/// classes that share nothing with the create ones except <c>LookupOwnership</c>, and
/// <c>UpdateEntity</c> rewrites all four FKs — so a fix applied only to create leaves the same hole one
/// HTTP verb away. That is the scenario most likely to be lost in a refactor.</para>
///
/// <para>The last test is the other half of SEC-2 and is deliberately green against today's behaviour
/// rather than the behaviour we want: a foreign lookup id that is <i>already</i> in the table (seeded
/// through the DbContext here, so the test survives the validator fix) makes the victim's own
/// <c>batch-delete</c> fail with a 409 they cannot explain or clear. The validators stop new rows;
/// cleaning up rows written before them is a data migration, not a code change.</para>
/// </summary>
[Collection("Postgres")]
public class ProfileForeignLookupIdTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string BacklogRoute = "api/activity-backlog-profile";
    private const string BucketListRoute = "api/activity-bucket-list-profile";
    private const string CostTierBatchDeleteRoute = "api/activity-expected-cost-tier/batch-delete";

    private const string LocationTypeField = "locationTypeId";
    private const string WeatherDependencyField = "weatherDependencyId";
    private const string ExpectedCostTierField = "expectedCostTierId";

    /// <summary>An id no user owns, for the "does not exist" half of the existence-oracle check.</summary>
    private const long NonExistentLookupId = 999999999L;

    private static string UniqueName(string prefix) => $"{prefix} {Guid.NewGuid():N}";

    private Task<long> CreateSecondUserAsync() =>
        ActivityProfileTestSeedHelper.CreateSecondUserAsync(Fixture, $"foreign-lookup-{Guid.NewGuid():N}@test.com");

    private static object BacklogPayload(long activityId, long locationTypeId, long weatherDependencyId,
        long expectedCostTierId, int durationMinutes = 45) => new
    {
        ActivityId = activityId,
        LocationTypeId = locationTypeId,
        WeatherDependencyId = weatherDependencyId,
        EnergyLevel = "Low",
        EffortType = "Mental",
        MinParticipants = 1,
        MaxParticipants = (int?)null,
        ExpectedCostTierId = expectedCostTierId,
        DurationMinutes = durationMinutes,
        IsRepeatable = false
    };

    private static object BucketListPayload(long activityId, long experienceTypeId, int comfortZoneStep = 3) => new
    {
        ActivityId = activityId,
        ExperienceTypeId = experienceTypeId,
        ComfortZoneStep = comfortZoneStep,
        RequiresTravel = false,
        FinancialGoal = (decimal?)null,
        InspirationSource = "A friend's story"
    };

    /// <summary>
    /// The caller's own activity plus a full set of their own lookups — the valid baseline every
    /// scenario below spoils exactly one field of.
    /// </summary>
    private async Task<(long activityId, long locationTypeId, long weatherDependencyId, long costTierId)>
        SeedOwnBacklogInputsAsync(string name)
    {
        await using var db = CreateDbContext();
        var userId = FakeLoggedUserService.TestUserId;
        return (
            await ActivityProfileTestSeedHelper.SeedActivityAsync(db, userId, name),
            await ActivityProfileTestSeedHelper.SeedLookupAsync<ActivityLocationType>(db, userId, $"{name} Home"),
            await ActivityProfileTestSeedHelper.SeedLookupAsync<ActivityWeatherDependency>(db, userId, $"{name} Indoor"),
            await ActivityProfileTestSeedHelper.SeedLookupAsync<ActivityExpectedCostTier>(db, userId, $"{name} Free"));
    }

    private async Task<(long locationTypeId, long weatherDependencyId, long costTierId)>
        SeedForeignBacklogLookupsAsync(long otherUserId, string name)
    {
        await using var db = CreateDbContext();
        return (
            await ActivityProfileTestSeedHelper.SeedLookupAsync<ActivityLocationType>(db, otherUserId, $"{name} Their Home"),
            await ActivityProfileTestSeedHelper.SeedLookupAsync<ActivityWeatherDependency>(db, otherUserId, $"{name} Their Indoor"),
            await ActivityProfileTestSeedHelper.SeedLookupAsync<ActivityExpectedCostTier>(db, otherUserId, $"{name} Expensive"));
    }

    // Fresh context, filters lifted: the profiles carry a user-scope filter of their own, and a write
    // that landed for the wrong user would otherwise read back as "no row" and pass the test for the
    // wrong reason.
    private async Task<ActivityBacklogProfile?> LoadBacklogByActivityAsync(long activityId)
    {
        await using var db = CreateDbContext();
        return await db.Set<ActivityBacklogProfile>().IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(p => p.ActivityId == activityId, CancellationToken);
    }

    private async Task<ActivityBucketListProfile?> LoadBucketListByActivityAsync(long activityId)
    {
        await using var db = CreateDbContext();
        return await db.Set<ActivityBucketListProfile>().IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(p => p.ActivityId == activityId, CancellationToken);
    }

    private async Task<ActivityBacklogProfile> LoadBacklogByIdAsync(long profileId)
    {
        await using var db = CreateDbContext();
        return await db.Set<ActivityBacklogProfile>().IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(p => p.Id == profileId, CancellationToken);
    }

    // ---- create: backlog -------------------------------------------------

    [Theory(DisplayName = "SEC-2: creating a backlog profile with another user's lookup id is refused and writes nothing")]
    [InlineData(LocationTypeField)]
    [InlineData(WeatherDependencyField)]
    [InlineData(ExpectedCostTierField)]
    public async Task Backlog_Create_WithAForeignLookupId_Returns400AndWritesNoRow(string foreignField)
    {
        // All three, not just one: they are three separate HasOne(...).OnDelete(Restrict) FKs written by
        // three separate Any()s, so a fix that covers one can miss the others.
        var otherUserId = await CreateSecondUserAsync();
        var name = UniqueName($"Backlog foreign {foreignField}");
        var (activityId, locationTypeId, weatherDependencyId, costTierId) = await SeedOwnBacklogInputsAsync(name);
        var (theirLocationTypeId, theirWeatherDependencyId, theirCostTierId) =
            await SeedForeignBacklogLookupsAsync(otherUserId, name);

        var payload = foreignField switch
        {
            LocationTypeField => BacklogPayload(activityId, theirLocationTypeId, weatherDependencyId, costTierId),
            WeatherDependencyField => BacklogPayload(activityId, locationTypeId, theirWeatherDependencyId, costTierId),
            ExpectedCostTierField => BacklogPayload(activityId, locationTypeId, weatherDependencyId, theirCostTierId),
            _ => throw new ArgumentOutOfRangeException(nameof(foreignField), foreignField, null)
        };

        var response = await CreateClient().PostAsJsonAsync(BacklogRoute, payload, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a lookup the caller does not own must be refused by the validator, not accepted by the FK");
        (await LoadBacklogByActivityAsync(activityId)).Should()
            .BeNull("the refusal is the whole point -- no row may reference another account's lookup");
    }

    [Fact(DisplayName = "SEC-2: a foreign lookup id and a non-existent one get the same answer, so the route is no existence oracle")]
    public async Task Backlog_Create_ForeignAndNonExistentLookupIds_AnswerIdentically()
    {
        // LookupOwnership uses one message for "does not exist" and "is not yours" on purpose. If the two
        // answers ever diverge, the endpoint becomes a probe over another account's id range -- the same
        // rule the tracker-mapping guard follows.
        var otherUserId = await CreateSecondUserAsync();
        var nameForeign = UniqueName("Backlog oracle foreign");
        var nameMissing = UniqueName("Backlog oracle missing");
        var foreignInputs = await SeedOwnBacklogInputsAsync(nameForeign);
        var missingInputs = await SeedOwnBacklogInputsAsync(nameMissing);
        var (_, _, theirCostTierId) = await SeedForeignBacklogLookupsAsync(otherUserId, nameForeign);

        var client = CreateClient();
        var foreignResponse = await client.PostAsJsonAsync(BacklogRoute,
            BacklogPayload(foreignInputs.activityId, foreignInputs.locationTypeId, foreignInputs.weatherDependencyId, theirCostTierId),
            JsonOpts, CancellationToken);
        var missingResponse = await client.PostAsJsonAsync(BacklogRoute,
            BacklogPayload(missingInputs.activityId, missingInputs.locationTypeId, missingInputs.weatherDependencyId, NonExistentLookupId),
            JsonOpts, CancellationToken);

        foreignResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        missingResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var foreignBody = await foreignResponse.Content.ReadAsStringAsync(CancellationToken);
        var missingBody = await missingResponse.Content.ReadAsStringAsync(CancellationToken);
        foreignBody.Should().Be(missingBody,
            "a caller must not be able to tell a stranger's id from an id nobody owns");

        (await LoadBacklogByActivityAsync(foreignInputs.activityId)).Should().BeNull();
        (await LoadBacklogByActivityAsync(missingInputs.activityId)).Should().BeNull();
    }

    [Fact(DisplayName = "SEC-2: creating a backlog profile with the caller's own lookup ids still works")]
    public async Task Backlog_Create_WithOwnedLookupIds_Returns201AndStoresTheIds()
    {
        // The positive control. Without it a validator that rejects everything passes every case above.
        var name = UniqueName("Backlog happy");
        var (activityId, locationTypeId, weatherDependencyId, costTierId) = await SeedOwnBacklogInputsAsync(name);

        var response = await CreateClient().PostAsJsonAsync(BacklogRoute,
            BacklogPayload(activityId, locationTypeId, weatherDependencyId, costTierId), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var newId = await response.Content.ReadFromJsonAsync<long>(JsonOpts, CancellationToken);
        newId.Should().BeGreaterThan(0);

        var stored = await LoadBacklogByActivityAsync(activityId);
        stored.Should().NotBeNull();
        stored!.Id.Should().Be(newId);
        stored.LocationTypeId.Should().Be(locationTypeId);
        stored.WeatherDependencyId.Should().Be(weatherDependencyId);
        stored.ExpectedCostTierId.Should().Be(costTierId);
        stored.DurationMinutes.Should().Be(45);
    }

    // ---- update: backlog -------------------------------------------------

    [Theory(DisplayName = "SEC-2: updating a backlog profile onto another user's lookup id is refused and the column is unchanged")]
    [InlineData(LocationTypeField)]
    [InlineData(WeatherDependencyField)]
    [InlineData(ExpectedCostTierField)]
    public async Task Backlog_Update_WithAForeignLookupId_Returns400AndLeavesTheRowAlone(string foreignField)
    {
        // UpdateEntity rewrites all three FKs, so the update is as much of a write surface for them as the
        // create -- and UpdateActivityBacklogProfileValidator shares no rule with the create validator.
        var otherUserId = await CreateSecondUserAsync();
        var name = UniqueName($"Backlog update foreign {foreignField}");

        long activityId, profileId;
        await using (var db = CreateDbContext())
            (activityId, profileId) =
                await ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db, FakeLoggedUserService.TestUserId, name);

        var before = await LoadBacklogByIdAsync(profileId);
        var (theirLocationTypeId, theirWeatherDependencyId, theirCostTierId) =
            await SeedForeignBacklogLookupsAsync(otherUserId, name);

        var payload = foreignField switch
        {
            LocationTypeField => BacklogPayload(activityId, theirLocationTypeId, before.WeatherDependencyId, before.ExpectedCostTierId, 99),
            WeatherDependencyField => BacklogPayload(activityId, before.LocationTypeId, theirWeatherDependencyId, before.ExpectedCostTierId, 99),
            ExpectedCostTierField => BacklogPayload(activityId, before.LocationTypeId, before.WeatherDependencyId, theirCostTierId, 99),
            _ => throw new ArgumentOutOfRangeException(nameof(foreignField), foreignField, null)
        };

        var response = await CreateClient()
            .PutAsJsonAsync($"{BacklogRoute}/{profileId}", payload, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var after = await LoadBacklogByIdAsync(profileId);
        after.LocationTypeId.Should().Be(before.LocationTypeId);
        after.WeatherDependencyId.Should().Be(before.WeatherDependencyId);
        after.ExpectedCostTierId.Should().Be(before.ExpectedCostTierId);
        after.DurationMinutes.Should().Be(before.DurationMinutes,
            "the request is rejected whole -- the fields that were legal must not be written either");
    }

    [Fact(DisplayName = "SEC-2: updating a backlog profile onto the caller's own lookup ids still works")]
    public async Task Backlog_Update_WithOwnedLookupIds_Returns200AndStoresTheIds()
    {
        var name = UniqueName("Backlog update happy");

        long activityId, profileId;
        long newLocationTypeId, newWeatherDependencyId, newCostTierId;
        await using (var db = CreateDbContext())
        {
            (activityId, profileId) =
                await ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db, FakeLoggedUserService.TestUserId, name);
            var userId = FakeLoggedUserService.TestUserId;
            newLocationTypeId = await ActivityProfileTestSeedHelper.SeedLookupAsync<ActivityLocationType>(db, userId, $"{name} Outdoors");
            newWeatherDependencyId = await ActivityProfileTestSeedHelper.SeedLookupAsync<ActivityWeatherDependency>(db, userId, $"{name} Dry only");
            newCostTierId = await ActivityProfileTestSeedHelper.SeedLookupAsync<ActivityExpectedCostTier>(db, userId, $"{name} Cheap");
        }

        var response = await CreateClient().PutAsJsonAsync($"{BacklogRoute}/{profileId}",
            BacklogPayload(activityId, newLocationTypeId, newWeatherDependencyId, newCostTierId, 60), JsonOpts, CancellationToken);

        // 200 with the id, not 204: BaseUpdateEndpoint answers Send.OkAsync(entity.Id).
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await LoadBacklogByIdAsync(profileId);
        after.LocationTypeId.Should().Be(newLocationTypeId);
        after.WeatherDependencyId.Should().Be(newWeatherDependencyId);
        after.ExpectedCostTierId.Should().Be(newCostTierId);
        after.DurationMinutes.Should().Be(60);
    }

    // ---- bucket list -----------------------------------------------------

    [Fact(DisplayName = "SEC-2: creating a bucket list profile with another user's experienceTypeId is refused and writes nothing")]
    public async Task BucketList_Create_WithAForeignExperienceTypeId_Returns400AndWritesNoRow()
    {
        var otherUserId = await CreateSecondUserAsync();
        var name = UniqueName("Bucket foreign experience");

        long activityId, theirExperienceTypeId;
        await using (var db = CreateDbContext())
        {
            activityId = await ActivityProfileTestSeedHelper.SeedActivityAsync(db, FakeLoggedUserService.TestUserId, name);
            theirExperienceTypeId =
                await ActivityProfileTestSeedHelper.SeedLookupAsync<ActivityExperienceType>(db, otherUserId, $"{name} Their Adventure");
        }

        var response = await CreateClient()
            .PostAsJsonAsync(BucketListRoute, BucketListPayload(activityId, theirExperienceTypeId), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LoadBucketListByActivityAsync(activityId)).Should().BeNull();
    }

    [Fact(DisplayName = "SEC-2: updating a bucket list profile onto another user's experienceTypeId is refused and the column is unchanged")]
    public async Task BucketList_Update_WithAForeignExperienceTypeId_Returns400AndLeavesTheRowAlone()
    {
        var otherUserId = await CreateSecondUserAsync();
        var name = UniqueName("Bucket update foreign");

        long activityId, profileId, theirExperienceTypeId;
        await using (var db = CreateDbContext())
        {
            (activityId, profileId) =
                await ActivityProfileTestSeedHelper.SeedBucketListProfileAsync(db, FakeLoggedUserService.TestUserId, name);
            theirExperienceTypeId =
                await ActivityProfileTestSeedHelper.SeedLookupAsync<ActivityExperienceType>(db, otherUserId, $"{name} Their Adventure");
        }

        var before = await LoadBucketListByActivityAsync(activityId);

        var response = await CreateClient().PutAsJsonAsync($"{BucketListRoute}/{profileId}",
            BucketListPayload(activityId, theirExperienceTypeId, 1), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var after = await LoadBucketListByActivityAsync(activityId);
        after!.ExperienceTypeId.Should().Be(before!.ExperienceTypeId);
        after.ComfortZoneStep.Should().Be(before.ComfortZoneStep);
    }

    [Fact(DisplayName = "SEC-2: the bucket list create and update still accept the caller's own experienceTypeId")]
    public async Task BucketList_CreateThenUpdate_WithOwnedExperienceTypeIds_Succeed()
    {
        var name = UniqueName("Bucket happy");

        long activityId, ownExperienceTypeId, secondOwnExperienceTypeId;
        await using (var db = CreateDbContext())
        {
            var userId = FakeLoggedUserService.TestUserId;
            activityId = await ActivityProfileTestSeedHelper.SeedActivityAsync(db, userId, name);
            ownExperienceTypeId =
                await ActivityProfileTestSeedHelper.SeedLookupAsync<ActivityExperienceType>(db, userId, $"{name} Adventure");
            secondOwnExperienceTypeId =
                await ActivityProfileTestSeedHelper.SeedLookupAsync<ActivityExperienceType>(db, userId, $"{name} Calm");
        }

        var client = CreateClient();
        var createResponse = await client
            .PostAsJsonAsync(BucketListRoute, BucketListPayload(activityId, ownExperienceTypeId), JsonOpts, CancellationToken);

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var profileId = await createResponse.Content.ReadFromJsonAsync<long>(JsonOpts, CancellationToken);
        (await LoadBucketListByActivityAsync(activityId))!.ExperienceTypeId.Should().Be(ownExperienceTypeId);

        var updateResponse = await client.PutAsJsonAsync($"{BucketListRoute}/{profileId}",
            BucketListPayload(activityId, secondOwnExperienceTypeId, 5), JsonOpts, CancellationToken);

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await LoadBucketListByActivityAsync(activityId);
        after!.ExperienceTypeId.Should().Be(secondOwnExperienceTypeId);
        after.ComfortZoneStep.Should().Be(5);
    }

    // ---- the other half of SEC-2: what a row that got through costs its victim ----

    [Fact(DisplayName = "SEC-2: a profile already pointing at another user's cost tier makes the owner's own batch-delete fail with a 409")]
    public async Task ForeignCostTierAlreadyPersisted_BlocksTheOwnersOwnBatchDelete()
    {
        // Seeded through the DbContext, not the API, deliberately: the validators now refuse to create such
        // a row, and this test is about the rows written before them. It is green because it states what
        // today's data actually does to the victim -- the tier is theirs, it is visible to them, the API
        // offers them the delete, and Postgres refuses it because of a row in an account they cannot see.
        // Clearing those rows is a data migration (MIG-3 shape), not a code change, which is why the
        // assertion below is the damage rather than the fix.
        var otherUserId = await CreateSecondUserAsync();
        var name = UniqueName("Victim pays");

        long theirCostTierId;
        await using (var db = CreateDbContext())
        {
            var (_, profileId) =
                await ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db, FakeLoggedUserService.TestUserId, name);
            theirCostTierId =
                await ActivityProfileTestSeedHelper.SeedLookupAsync<ActivityExpectedCostTier>(db, otherUserId, $"{name} Expensive");

            var profile = await db.Set<ActivityBacklogProfile>().IgnoreQueryFilters()
                .SingleAsync(p => p.Id == profileId, CancellationToken);
            profile.ExpectedCostTierId = theirCostTierId;
            await db.SaveChangesAsync(CancellationToken);
        }

        await using var factoryB = CreateFactory(["User"], otherUserId);
        var response = await factoryB.CreateClient().PostAsJsonAsync(CostTierBatchDeleteRoute,
            new { ids = new[] { theirCostTierId } }, JsonOpts, CancellationToken);

        // 409 via DbUtils.HandleException: the three profile lookup FKs are DeleteBehavior.Restrict, so
        // the delete reaches Postgres and comes back as a foreign key violation.
        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "the Restrict FK refuses a delete the owner is fully entitled to make");

        await using var verifyDb = CreateDbContext();
        var survived = await verifyDb.Set<ActivityExpectedCostTier>().IgnoreQueryFilters()
            .AnyAsync(t => t.Id == theirCostTierId, CancellationToken);
        survived.Should().BeTrue("the tier is pinned by a profile its owner has no visibility of");
    }
}
