using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Sydowwe.Framework.Testing.baseTests;

public abstract class BaseGridEndpointTests(IPostgresFixture fixture)
    : PostgresTestBase(fixture)
{
    protected abstract string EndpointUrl { get; }

    // Called inside HappyPath_ReturnsSeededEntity â€” seed your entity and return its id.
    // Emp (if attendance) is already set via SeedAsync before this runs.
    protected abstract Task<long> SeedEntityAsync(DbContext db);

    protected static object GridBody(bool useFilter = false, object? filter = null) =>
        new
        {
            useFilter,
            filter = filter ?? new { },
            sortBy = Array.Empty<object>(),
            itemsPerPage = 20,
            page = 1
        };

    // Mirrors BaseGridResponse<T> (Items / ItemsCount / PageCount). Do NOT use TotalCount here --
    // the server never serializes that field, so it would silently deserialize to 0 (DOC-1).
    protected record GridResponse(JsonElement[] Items, int ItemsCount, int PageCount);

    [Fact]
    public async Task HappyPath_ReturnsSeededEntity()
    {
        await using var db = CreateDbContext();
        var id = await SeedEntityAsync(db);

        var response = await CreateClient().PostAsJsonAsync(EndpointUrl, GridBody(), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GridResponse>(JsonOpts);
        body!.Items.Should().Contain(i => i.GetProperty("id").GetInt64() == id);
        body.ItemsCount.Should().BeGreaterThan(0, "the seeded entity is counted");
        body.PageCount.Should().BeGreaterThan(0, "at least one page of results exists");
    }

    [Fact]
    public async Task EmptyRequest_Returns200WithEmptyPage()
    {
        var response = await CreateClient().PostAsJsonAsync(EndpointUrl, GridBody(), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GridResponse>(JsonOpts);
        body.Should().NotBeNull();
        body.Items.Should().NotBeNull();
        body.ItemsCount.Should().BeGreaterThanOrEqualTo(0);
        body.PageCount.Should().BeGreaterThanOrEqualTo(0);
    }

    // virtual: grids that are deliberately widened beyond Admin (e.g. a manager team-view that scopes
    // results in ApplyUserScoping) must override this to assert their actual auth/scoping contract.
    [Fact(DisplayName = "User role hits the endpoint's role gate (403 only when admin-only)")]
    public virtual async Task UserRole_MatchesRoleGate()
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync(EndpointUrl, GridBody(), JsonOpts);

        AssertUserRoleGate(response);
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        var response = await CreateUnauthenticatedClient().PostAsJsonAsync(EndpointUrl, GridBody(), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}