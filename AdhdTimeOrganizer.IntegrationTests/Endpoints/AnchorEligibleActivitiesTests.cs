using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// B12 — <c>GET /api/memory-anchor/anchor-eligible-activities</c>, the picker behind the unlocked
/// <i>Add</i> on the memory-anchors page. The route did not exist for as long as the client called it, and
/// what the client saw was <c>GetByIdMemoryAnchorEndpoint</c> answering 400 for a path it took as
/// <c>/{id}</c> — so the first assertion is simply that the route is its own.
/// </summary>
/// <remarks>
/// The eligibility rule is a hand-written copy of <c>CreateMemoryAnchorValidator</c>'s: own activity, not
/// archived, backlog or bucket-list profile present. Every predicate is a <c>Where</c> the endpoint could lose
/// without a compile error, and the host's global filters would then cover two of the three in production
/// only — which is why each one gets a row-level assertion here rather than a 200.
/// </remarks>
[Collection("Postgres")]
public class AnchorEligibleActivitiesTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string Route = "api/memory-anchor/anchor-eligible-activities";

    private static long UserId => FakeLoggedUserService.TestUserId;

    private sealed record Option(long Id, string Text);

    private async Task<List<Option>> GetOptionsAsync()
    {
        var response = await CreateClient().GetAsync(Route, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<Option>>(JsonOpts, CancellationToken))!;
    }

    [Fact(DisplayName = "The route is registered as its own, not swallowed by GetById's /{id}")]
    public async Task Route_IsNotCaughtByGetById()
    {
        var response = await CreateClient().GetAsync(Route, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "before the endpoint existed this path fell into /memory-anchor/{id} and answered 400 for a non-numeric id");
    }

    [Fact(DisplayName = "Backlog and bucket-list activities are listed; a bare one and a project-only one are not")]
    public async Task ListsExactlyTheProfilesTheCreateValidatorAccepts()
    {
        long backlogId, repeatableBacklogId, bucketId, projectId, bareId;
        await using (var db = CreateDbContext())
        {
            (backlogId, _) = await ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db, UserId, "Eligible one-time backlog");
            (repeatableBacklogId, _) = await ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db, UserId, "Eligible repeatable backlog", isRepeatable: true);
            (bucketId, _) = await ActivityProfileTestSeedHelper.SeedBucketListProfileAsync(db, UserId, "Eligible bucket");
            (projectId, _) = await ActivityProfileTestSeedHelper.SeedProjectProfileAsync(db, UserId, "Ineligible project");
            bareId = await ActivityProfileTestSeedHelper.SeedActivityAsync(db, UserId, "Ineligible bare");
        }

        var ids = (await GetOptionsAsync()).Select(o => o.Id).ToList();

        ids.Should().Contain([backlogId, bucketId]);
        ids.Should().Contain(repeatableBacklogId,
            "the create validator accepts any backlog profile, repeatable or not, and the picker must agree with it");
        ids.Should().NotContain(projectId, "a project profile is DIY, not an experience — the validator refuses it");
        ids.Should().NotContain(bareId, "an activity with no profile at all cannot be anchored");
    }

    [Fact(DisplayName = "The option text is the activity name and the list is ordered by it")]
    public async Task Options_CarryTheActivityName_OrderedByName()
    {
        long zId, aId;
        await using (var db = CreateDbContext())
        {
            (zId, _) = await ActivityProfileTestSeedHelper.SeedBucketListProfileAsync(db, UserId, "Zz ordering probe");
            (aId, _) = await ActivityProfileTestSeedHelper.SeedBucketListProfileAsync(db, UserId, "Aa ordering probe");
        }

        var options = await GetOptionsAsync();

        options.Should().ContainSingle(o => o.Id == aId).Which.Text.Should().Be("Aa ordering probe");
        options.Should().ContainSingle(o => o.Id == zId).Which.Text.Should().Be("Zz ordering probe");
        // Relative position rather than a whole-list ordinal check: the database sorts by its own collation
        // and the shared fixture holds every other suite's rows.
        var ids = options.Select(o => o.Id).ToList();
        ids.IndexOf(aId).Should().BeLessThan(ids.IndexOf(zId),
            "the picker is a VIdAutocomplete and sorts nothing itself");
    }

    [Fact(DisplayName = "An activity that already has an anchor stays listed")]
    public async Task AlreadyAnchoredActivity_StaysListed()
    {
        long activityId;
        await using (var db = CreateDbContext())
        {
            (activityId, _) = await ActivityProfileTestSeedHelper.SeedBucketListProfileAsync(db, UserId, "Already anchored bucket");
            await ActivityProfileTestSeedHelper.SeedMemoryAnchorAsync(db, UserId, activityId, 2026, 3);
        }

        var ids = (await GetOptionsAsync()).Select(o => o.Id);

        ids.Should().Contain(activityId,
            "an anchor is a memorable month, not a completion flag — the only ceiling is two per month, and Create enforces it");
    }

    [Fact(DisplayName = "An archived activity is not listed")]
    public async Task ArchivedActivity_IsNotListed()
    {
        long activityId;
        await using (var db = CreateDbContext())
        {
            (activityId, _) = await ActivityProfileTestSeedHelper.SeedBucketListProfileAsync(db, UserId, "Archived bucket");
            var activity = await db.Set<Activity>().SingleAsync(a => a.Id == activityId, CancellationToken);
            activity.IsArchived = true;
            await db.SaveChangesAsync(CancellationToken);
        }

        var ids = (await GetOptionsAsync()).Select(o => o.Id);

        ids.Should().NotContain(activityId, "nothing new can be recorded against a retired activity");
    }

    [Fact(DisplayName = "Another user's eligible activity is not listed")]
    public async Task ForeignActivity_IsNotListed()
    {
        var otherUserId = await ActivityProfileTestSeedHelper.CreateSecondUserAsync(fixture, "anchor-eligible-other@test.local");

        long foreignId;
        await using (var db = CreateDbContext())
        {
            (foreignId, _) = await ActivityProfileTestSeedHelper.SeedBucketListProfileAsync(db, otherUserId, "Foreign bucket");
        }

        var ids = (await GetOptionsAsync()).Select(o => o.Id);

        ids.Should().NotContain(foreignId, "the scoping is a hand-written UserId predicate, and nothing else in the slice covers it");
    }
}
