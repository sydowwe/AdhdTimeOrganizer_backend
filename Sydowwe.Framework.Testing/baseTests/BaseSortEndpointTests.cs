using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Sydowwe.Framework.Testing.baseTests;

public abstract class BaseSortEndpointTests(IPostgresFixture fixture)
    : PostgresTestBase(fixture)
{
    // e.g. "/api/complaint/sort"
    protected abstract string EndpointUrl { get; }

    // Seed one entity and return its id.
    protected abstract Task<long> SeedEntityAsync(DbContext db);

    // BaseSortRequest shape: { sortBy: [] }
    protected static object EmptySortPayload() => new { sortBy = Array.Empty<object>() };

    [Fact]
    public async Task HappyPath_Admin_Returns200WithSeededEntity()
    {
        await using var db = CreateDbContext();
        var id = await SeedEntityAsync(db);

        var response = await CreateClient().PostAsJsonAsync(EndpointUrl, EmptySortPayload(), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<JsonElement[]>(JsonOpts);
        items!.Should().Contain(i => i.GetProperty("id").GetInt64() == id);
    }

    [Fact(DisplayName = "User role hits the endpoint's role gate (403 only when admin-only)")]
    public async Task UserRole_MatchesRoleGate()
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync(EndpointUrl, EmptySortPayload(), JsonOpts);

        AssertUserRoleGate(response);
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        var response = await CreateUnauthenticatedClient().PostAsJsonAsync(EndpointUrl, EmptySortPayload(), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}