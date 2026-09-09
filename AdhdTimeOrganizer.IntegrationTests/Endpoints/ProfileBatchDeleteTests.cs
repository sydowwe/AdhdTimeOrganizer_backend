using AdhdTimeOrganizer.Core.infrastructure.persistence;
using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.entityInterface;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-2 — the three <c>batch-delete</c> routes for the Activity*Profile families, which nothing
/// exercised before this suite.
///
/// <para>What fails silently: the profiles are not <c>IEntityWithUser</c>, so nothing about them is
/// scoped by the framework's own machinery. Two hand-built things keep another account's rows out of
/// the batch — the named <c>AppQueryFilters.ActivityProfileUserScope</c> filter walking
/// <c>Activity.UserId</c> (<c>AppDbContext.cs:339-350</c>), and each endpoint's <c>AuthorizeAsync</c>
/// override behind it. Lose either and every route still answers 204 on the happy path, still 404s on
/// a bad id, and quietly deletes whatever ids the caller cared to name. So the cross-user case below
/// asserts that the <b>row survives</b>, not merely that the status code was a refusal: a rewrite
/// that deletes what it can and refuses afterwards passes a status-code-only test. The refusal is
/// all-or-nothing because <c>BaseBatchDeleteEndpoint</c> fetches, then authorizes, then saves once.
/// Note the test cannot tell which of the two guards refused, only that the batch did — the filter
/// answers first, which is why a foreign id reads as 404 rather than the base's 403.</para>
///
/// <para>The <b>empty</b> id list is pinned explicitly at <b>400</b> — the base's behaviour
/// (<c>BaseBatchDeleteEndpoint</c>, "No ids were provided."), not the 204 the superseded hand-written
/// loop used to give.</para>
///
/// <para>Cascade direction: deleting a profile must NOT take its <c>Activity</c> with it — the FK runs
/// the other way (cascade from activity to profile), which
/// <c>ActivityProfileGridTests.DeletingActivity_CascadesToItsProfile_NoOrphanSurvives</c> pins.</para>
/// </summary>
public abstract class ProfileBatchDeleteTestsBase<TProfile>(AppDbContextFixture fixture) : PostgresTestBase(fixture)
    where TProfile : class, IEntityWithId
{
    /// <summary>e.g. <c>api/activity-backlog-profile/batch-delete</c>.</summary>
    protected abstract string Route { get; }

    protected abstract Task<(long activityId, long profileId)> SeedProfileAsync(DbContext db, long userId, string name);

    private static object Body(params long[] ids) => new { ids };

    private Task<HttpResponseMessage> PostAsync(HttpClient client, params long[] ids) =>
        client.PostAsJsonAsync(Route, Body(ids), JsonOpts, CancellationToken);

    private async Task<bool> ProfileExistsAsync(long profileId)
    {
        // A fresh context every time: the seeding context's change tracker would answer from memory and
        // make a delete that never happened look like it did (and vice versa).
        await using var db = CreateDbContext();
        return await db.Set<TProfile>().IgnoreQueryFilters().AnyAsync(p => p.Id == profileId, CancellationToken);
    }

    private async Task<bool> ActivityExistsAsync(long activityId)
    {
        await using var db = CreateDbContext();
        return await db.Set<Activity>().IgnoreQueryFilters().AnyAsync(a => a.Id == activityId, CancellationToken);
    }

    private static string UniqueName(string prefix) => $"{prefix} {Guid.NewGuid():N}";

    [Fact(DisplayName = "TEST-2: batch-delete removes every id the caller owns and leaves their Activities alone")]
    public async Task BatchDelete_TwoOwnedProfiles_DeletesBothAndKeepsTheActivities()
    {
        long activityIdOne, profileIdOne, activityIdTwo, profileIdTwo;
        await using (var db = CreateDbContext())
        {
            (activityIdOne, profileIdOne) =
                await SeedProfileAsync(db, FakeLoggedUserService.TestUserId, UniqueName("Batch own one"));
            (activityIdTwo, profileIdTwo) =
                await SeedProfileAsync(db, FakeLoggedUserService.TestUserId, UniqueName("Batch own two"));
        }

        var response = await PostAsync(CreateClient(), profileIdOne, profileIdTwo);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ProfileExistsAsync(profileIdOne)).Should().BeFalse();
        (await ProfileExistsAsync(profileIdTwo)).Should().BeFalse();

        // The FK cascades activity -> profile, never profile -> activity.
        (await ActivityExistsAsync(activityIdOne)).Should().BeTrue("deleting a profile must not delete its Activity");
        (await ActivityExistsAsync(activityIdTwo)).Should().BeTrue("deleting a profile must not delete its Activity");
    }

    [Fact(DisplayName = "TEST-2: one foreign id refuses the whole batch and the caller's own row survives")]
    public async Task BatchDelete_WithAForeignId_RefusesEverythingAndDeletesNothing()
    {
        var otherUserId =
            await ActivityProfileTestSeedHelper.CreateSecondUserAsync(Fixture, $"batch-delete-{Guid.NewGuid():N}@test.com");

        long ownProfileId, foreignProfileId;
        await using (var db = CreateDbContext())
        {
            (_, ownProfileId) = await SeedProfileAsync(db, FakeLoggedUserService.TestUserId, UniqueName("Batch mine"));
            (_, foreignProfileId) = await SeedProfileAsync(db, otherUserId, UniqueName("Batch theirs"));
        }

        var response = await PostAsync(CreateClient(), ownProfileId, foreignProfileId);

        // 404, not the base's 403: ActivityProfileUserScope hides the foreign row, so the fetched
        // count never matches and the batch stops before AuthorizeAsync is ever consulted. Same shape
        // as ActivityCategoryBatchDeleteTests, and the one that does not leak whether the id exists.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // The assertion that actually matters. A batch that deleted the owned id and then refused
        // would pass on the status code alone.
        (await ProfileExistsAsync(ownProfileId)).Should()
            .BeTrue("the refusal is all-or-nothing -- nothing is saved once any id fails authorization");
        (await ProfileExistsAsync(foreignProfileId)).Should().BeTrue("another user's row must never be deleted");
    }

    [Fact(DisplayName = "TEST-2: a non-existent id refuses the whole batch and the caller's own row survives")]
    public async Task BatchDelete_WithANonExistentId_RefusesEverythingAndDeletesNothing()
    {
        long ownProfileId;
        await using (var db = CreateDbContext())
            (_, ownProfileId) = await SeedProfileAsync(db, FakeLoggedUserService.TestUserId, UniqueName("Batch missing peer"));

        var response = await PostAsync(CreateClient(), ownProfileId, 999999L);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ProfileExistsAsync(ownProfileId)).Should()
            .BeTrue("the fetched count never matches, so the batch stops before RemoveRange");
    }

    [Fact(DisplayName = "TEST-2: an empty id list is a 400, matching BaseBatchDeleteEndpoint")]
    public async Task BatchDelete_EmptyIdList_Returns400()
    {
        // Pinned explicitly: the hand-written loop these endpoints used to run answered 204 here, and
        // the migration onto the base changed it silently.
        var response = await PostAsync(CreateClient());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "TEST-2: batch-delete is closed to anonymous callers")]
    public async Task BatchDelete_Unauthenticated_Returns401()
    {
        long ownProfileId;
        await using (var db = CreateDbContext())
            (_, ownProfileId) = await SeedProfileAsync(db, FakeLoggedUserService.TestUserId, UniqueName("Batch anon"));

        var response = await PostAsync(CreateUnauthenticatedClient(), ownProfileId);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ProfileExistsAsync(ownProfileId)).Should().BeTrue();
    }
}

[Collection("Postgres")]
public class ActivityBacklogProfileBatchDeleteTests(AppDbContextFixture fixture)
    : ProfileBatchDeleteTestsBase<ActivityBacklogProfile>(fixture)
{
    protected override string Route => "api/activity-backlog-profile/batch-delete";

    protected override Task<(long activityId, long profileId)> SeedProfileAsync(DbContext db, long userId, string name) =>
        ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db, userId, name);
}

[Collection("Postgres")]
public class ActivityBucketListProfileBatchDeleteTests(AppDbContextFixture fixture)
    : ProfileBatchDeleteTestsBase<ActivityBucketListProfile>(fixture)
{
    protected override string Route => "api/activity-bucket-list-profile/batch-delete";

    protected override Task<(long activityId, long profileId)> SeedProfileAsync(DbContext db, long userId, string name) =>
        ActivityProfileTestSeedHelper.SeedBucketListProfileAsync(db, userId, name);
}

[Collection("Postgres")]
public class ActivityProjectProfileBatchDeleteTests(AppDbContextFixture fixture)
    : ProfileBatchDeleteTestsBase<ActivityProjectProfile>(fixture)
{
    protected override string Route => "api/activity-project-profile/batch-delete";

    protected override Task<(long activityId, long profileId)> SeedProfileAsync(DbContext db, long userId, string name) =>
        ActivityProfileTestSeedHelper.SeedProjectProfileAsync(db, userId, name);
}
