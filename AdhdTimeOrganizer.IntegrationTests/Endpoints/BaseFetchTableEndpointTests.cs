using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Sydowwe.Framework.Testing.baseTests;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

// Tests BaseGridEndpoint via GridActivityCategoryEndpoint ("/activity-category/filtered-table")
[Collection("Postgres")]
public class GridActivityCategoryEndpointTests(AppDbContextFixture fixture)
    : BaseGridEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-category/filtered-table";

    // ActivityCategory is user-owned data; the endpoint does not override AllowedRoles(), so plain
    // Users can reach it.
    protected override bool IsAdminOnly => false;

    protected override async Task<long> SeedEntityAsync(DbContext db)
    {
        var category = new ActivityCategory { Name = "Table A", Color = "#FF0000", UserId = FakeLoggedUserService.TestUserId };
        ((AppDbContext)db).ActivityCategories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    /// <summary>
    /// The <c>role</c> column is the one field on this grid with no column behind it — a category has no
    /// role, it borrows the roles of the activities filed under it, joined by <c>string_agg</c> inside the
    /// projection. Two things can break without an error: the join stops translating (500 on a request the
    /// client is right to make), or the value is quietly computed after the projection, in which case it
    /// still renders but sorts on an empty string. Both are invisible unless the row itself is asserted.
    /// </summary>
    [Fact]
    public async Task Role_JoinsTheDistinctRolesOfTheCategorysActivities_AndSkipsArchivedOnes()
    {
        await using var db = CreateDbContext();
        var userId = FakeLoggedUserService.TestUserId;
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var category = new ActivityCategory { Name = $"Roles {suffix}", Color = "#00FF00", UserId = userId };
        db.Set<ActivityCategory>().Add(category);

        var work = new ActivityRole { UserId = userId, Name = $"Work {suffix}", Color = "#112233" };
        var personal = new ActivityRole { UserId = userId, Name = $"Personal {suffix}", Color = "#332211" };
        var retired = new ActivityRole { UserId = userId, Name = $"Retired {suffix}", Color = "#222222" };
        db.Set<ActivityRole>().AddRange(work, personal, retired);
        await db.SaveChangesAsync(CancellationToken);

        db.Set<Activity>().AddRange(
            new Activity { UserId = userId, Name = $"A {suffix}", RoleId = work.Id, CategoryId = category.Id },
            // Second activity on the same role: the role must appear once, not twice.
            new Activity { UserId = userId, Name = $"B {suffix}", RoleId = work.Id, CategoryId = category.Id },
            new Activity { UserId = userId, Name = $"C {suffix}", RoleId = personal.Id, CategoryId = category.Id },
            // Archived: hidden by the ActivityArchived query filter, which must reach into the subquery.
            new Activity { UserId = userId, Name = $"D {suffix}", RoleId = retired.Id, CategoryId = category.Id, IsArchived = true });
        await db.SaveChangesAsync(CancellationToken);

        var row = await FetchRowAsync(category.Id);

        // Alphabetical, so "Personal" leads regardless of insertion order.
        row.GetProperty("role").GetString().Should().Be($"Personal {suffix}, Work {suffix}");
    }

    [Fact]
    public async Task Role_IsEmpty_ForACategoryWithNoActivities()
    {
        await using var db = CreateDbContext();
        var category = new ActivityCategory
        {
            Name = $"Empty {Guid.NewGuid().ToString("N")[..8]}", Color = "#0000FF", UserId = FakeLoggedUserService.TestUserId
        };
        db.Set<ActivityCategory>().Add(category);
        await db.SaveChangesAsync(CancellationToken);

        var row = await FetchRowAsync(category.Id);

        row.GetProperty("role").GetString().Should().BeEmpty();
    }

    /// <summary>
    /// The request that threw the original untranslatable-LINQ 500, and the reason
    /// <c>PreprocessSortBy</c> exists on this endpoint: <c>role</c> is a client-side <c>string.Join</c>
    /// and can never reach an <c>ORDER BY</c>, so the key is redirected onto <c>RoleSortKey</c>.
    /// </summary>
    /// <remarks>
    /// Asserting the status alone would not catch a regression: drop the mapping and this is a 500, but
    /// drop <em>only</em> the ordering — map the key onto something constant, say — and it stays a
    /// cheerful 200 in the wrong order. So this asserts the order of the rows.
    /// </remarks>
    [Fact]
    public async Task SortingByRole_OrdersByTheLeadingRoleName()
    {
        await using var db = CreateDbContext();
        var userId = FakeLoggedUserService.TestUserId;
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // Names chosen so alphabetical order by role is the reverse of insertion order.
        var (zulu, mike, alpha) = (
            new ActivityRole { UserId = userId, Name = $"Zulu {suffix}", Color = "#111111" },
            new ActivityRole { UserId = userId, Name = $"Mike {suffix}", Color = "#222222" },
            new ActivityRole { UserId = userId, Name = $"Alpha {suffix}", Color = "#333333" });
        db.Set<ActivityRole>().AddRange(zulu, mike, alpha);

        var first = new ActivityCategory { Name = $"Cat Z {suffix}", Color = "#FFFFFF", UserId = userId };
        var second = new ActivityCategory { Name = $"Cat M {suffix}", Color = "#FFFFFF", UserId = userId };
        var third = new ActivityCategory { Name = $"Cat A {suffix}", Color = "#FFFFFF", UserId = userId };
        db.Set<ActivityCategory>().AddRange(first, second, third);
        await db.SaveChangesAsync(CancellationToken);

        db.Set<Activity>().AddRange(
            new Activity { UserId = userId, Name = $"z {suffix}", RoleId = zulu.Id, CategoryId = first.Id },
            new Activity { UserId = userId, Name = $"m {suffix}", RoleId = mike.Id, CategoryId = second.Id },
            new Activity { UserId = userId, Name = $"a {suffix}", RoleId = alpha.Id, CategoryId = third.Id });
        await db.SaveChangesAsync(CancellationToken);

        var seeded = new[] { first.Id, second.Id, third.Id };

        var ascending = await FetchIdsAsync(isDesc: false, seeded);
        ascending.Should().Equal([third.Id, second.Id, first.Id], "Alpha < Mike < Zulu");

        var descending = await FetchIdsAsync(isDesc: true, seeded);
        descending.Should().Equal([first.Id, second.Id, third.Id], "the direction flag still reaches the proxy");
    }

    /// <summary>
    /// The proxy is deliberately not serialized, so the client never sees a second role field it might
    /// render or sort by directly.
    /// </summary>
    [Fact]
    public async Task RoleSortKey_IsNotSerialized()
    {
        await using var db = CreateDbContext();
        var id = await SeedEntityAsync(db);

        var row = await FetchRowAsync(id);

        row.TryGetProperty("roleSortKey", out _).Should().BeFalse();
    }

    private async Task<long[]> FetchIdsAsync(bool isDesc, long[] keepOnly)
    {
        var response = await CreateClient().PostAsJsonAsync(EndpointUrl, new
        {
            page = 1,
            itemsPerPage = 200,
            sortBy = new[] { new { key = "role", isDesc } },
            useFilter = false,
            filter = new { }
        }, JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GridResponse>(JsonOpts, CancellationToken);
        return body!.Items
            .Select(i => i.GetProperty("id").GetInt64())
            .Where(keepOnly.Contains)
            .ToArray();
    }

    private async Task<JsonElement> FetchRowAsync(long categoryId)
    {
        var response = await CreateClient().PostAsJsonAsync(EndpointUrl, new
        {
            page = 1,
            itemsPerPage = 200,
            sortBy = new[] { new { key = "role", isDesc = false } },
            useFilter = false,
            filter = new { }
        }, JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GridResponse>(JsonOpts, CancellationToken);
        return body!.Items.Should().ContainSingle(i => i.GetProperty("id").GetInt64() == categoryId).Subject;
    }
}
