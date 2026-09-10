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
/// PERF-2 — the three grids' free-text filters run as <c>ILIKE</c>, so they match regardless of case
/// and treat <c>%</c> / <c>_</c> as literals.
/// </summary>
/// <remarks>
/// <para>
/// This is the failure mode with no error attached to it: <c>string.Contains</c> renders as
/// <c>LIKE '%x%'</c>, which Postgres evaluates case-sensitively, so a user with <c>Deep Work</c> types
/// <c>deep</c> and gets an empty grid. The row exists, the filter "works", nothing is logged, and the
/// only conclusion available to the user is that search is broken.
/// </para>
/// <para>
/// The escaping cases matter as much as the casing ones: <c>EF.Functions.ILike</c> does <b>not</b>
/// escape LIKE metacharacters the way <c>Contains</c> did, so a hand-rolled interpolated pattern turns
/// a typed <c>%</c> into a wildcard. That is why <c>FreeTextFilterExtensions.ToContainsPattern</c>
/// exists and why a wrong row — not merely a too-large result — is what case 5 looks for.
/// </para>
/// <para>
/// ⚠ <c>useFilter: true</c> is load-bearing on every request here: <c>BaseGridEndpoint</c> skips
/// <c>ApplyCustomFiltering</c> entirely when it is false, so a filter object sent with
/// <c>useFilter: false</c> silently applies nothing and every assertion below would pass vacuously.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class GridTextFilterTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private static long UserId => FakeLoggedUserService.TestUserId;

    [Theory(DisplayName = "PERF-2: the activity grid's name filter matches regardless of case")]
    [InlineData("deep")]
    [InlineData("DEEP")]
    [InlineData("DeEp")]
    [InlineData("Deep")]
    public async Task ActivityGrid_NameFilter_IsCaseInsensitive(string typed)
    {
        long activityId;
        await using (var db = CreateDbContext())
            activityId = await SeedActivityAsync(db, $"Deep Work {Guid.NewGuid():N}");

        var ids = await FetchIdsAsync("api/activity/grid", new { Name = typed });

        ids.Should().Contain(activityId,
            "Postgres LIKE is case-sensitive, so a Contains-based filter would answer an empty grid for every casing but the row's own");
    }

    /// <summary>
    /// The pin against over-fixing. The natural way to overshoot case sensitivity is to loosen the
    /// predicate itself — splitting on whitespace and OR-ing the terms, say — and a non-matching filter
    /// returning rows is the only symptom that has.
    /// </summary>
    [Fact(DisplayName = "PERF-2: a non-matching name filter still returns nothing")]
    public async Task ActivityGrid_NonMatchingNameFilter_ReturnsNothing()
    {
        await using (var db = CreateDbContext())
            await SeedActivityAsync(db, $"Deep Work {Guid.NewGuid():N}");

        var ids = await FetchIdsAsync("api/activity/grid", new { Name = "Shallow" });

        ids.Should().BeEmpty("case-insensitivity is not a licence to match more rows");
    }

    [Theory(DisplayName = "PERF-2: the role and category grids filter case-insensitively too")]
    [InlineData("api/activity-role/grid")]
    [InlineData("api/activity-category/grid")]
    public async Task LookupGrids_NameFilter_IsCaseInsensitive(string route)
    {
        long id;
        var name = $"Deep Focus {Guid.NewGuid():N}";
        await using (var db = CreateDbContext())
            id = route.StartsWith("api/activity-role", StringComparison.Ordinal)
                ? await SeedRoleAsync(db, UserId, name)
                : await SeedCategoryAsync(db, UserId, name);

        var ids = await FetchIdsAsync(route, new { Name = "deep focus" });

        ids.Should().Contain(id, "three grids, one rule — a fix applied only to the busiest one is the likely half-fix");
    }

    /// <summary>
    /// <c>%</c> typed into the filter is a literal, not a wildcard. The second seeded row is what turns
    /// an unescaped pattern into a visibly <em>wrong</em> row rather than a merely generous result.
    /// </summary>
    [Fact(DisplayName = "PERF-2: LIKE metacharacters in the filter are matched literally")]
    public async Task ActivityGrid_NameFilter_TreatsPercentAndUnderscoreAsLiterals()
    {
        long literalId, decoyId, underscoreId, underscoreDecoyId;
        await using (var db = CreateDbContext())
        {
            literalId = await SeedActivityAsync(db, $"100% Focus {Guid.NewGuid():N}");
            decoyId = await SeedActivityAsync(db, $"1005 Focus {Guid.NewGuid():N}");
            underscoreId = await SeedActivityAsync(db, $"deep_work {Guid.NewGuid():N}");
            underscoreDecoyId = await SeedActivityAsync(db, $"deepXwork {Guid.NewGuid():N}");
        }

        var percentMatches = await FetchIdsAsync("api/activity/grid", new { Name = "100%" });
        percentMatches.Should().Contain(literalId).And.NotContain(decoyId,
            "an unescaped % is a wildcard, and '1005 Focus' is the row that makes that visible");

        var underscoreMatches = await FetchIdsAsync("api/activity/grid", new { Name = "deep_work" });
        underscoreMatches.Should().Contain(underscoreId).And.NotContain(underscoreDecoyId,
            "an unescaped _ matches any single character");
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private async Task<List<long>> FetchIdsAsync(string route, object filter)
    {
        var response = await CreateClient().PostAsJsonAsync(route, new
        {
            Page = 1,
            ItemsPerPage = 100,
            SortBy = Array.Empty<object>(),
            UseFilter = true,
            Filter = filter
        }, JsonOpts, CancellationToken);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        return JsonDocument.Parse(body).RootElement.GetProperty("items")
            .EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).ToList();
    }

    private async Task<long> SeedRoleAsync(DbContext db, long userId, string name)
    {
        var role = new ActivityRole { UserId = userId, Name = name, Color = "#112233" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);
        return role.Id;
    }

    private async Task<long> SeedCategoryAsync(DbContext db, long userId, string name)
    {
        var category = new ActivityCategory { UserId = userId, Name = name, Color = "#445566" };
        db.Set<ActivityCategory>().Add(category);
        await db.SaveChangesAsync(CancellationToken);
        return category.Id;
    }

    private async Task<long> SeedActivityAsync(DbContext db, string name)
    {
        var activity = new Activity
        {
            UserId = UserId,
            Name = name,
            RoleId = await SeedRoleAsync(db, UserId, $"{name} Role {Guid.NewGuid():N}")
        };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);
        return activity.Id;
    }
}