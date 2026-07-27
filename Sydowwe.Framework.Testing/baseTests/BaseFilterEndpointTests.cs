using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Sydowwe.Framework.Testing.baseTests;

public abstract class BaseFilterEndpointTests(IPostgresFixture fixture)
    : PostgresTestBase(fixture)
{
    // e.g. "/api/complaint/filter"
    protected abstract string EndpointUrl { get; }

    // Seed one entity and return its id.
    protected abstract Task<long> SeedEntityAsync(DbContext db);

    // Build the filter payload (TFilter, IFilterRequest shape). Default is an empty object â€” endpoints
    // that don't tolerate that should override.
    protected virtual object EmptyFilterPayload() => new { };

    [Fact]
    public async Task HappyPath_Admin_Returns200WithSeededEntity()
    {
        await using var db = CreateDbContext();
        var id = await SeedEntityAsync(db);

        var response = await CreateClient().PostAsJsonAsync(EndpointUrl, EmptyFilterPayload(), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<JsonElement[]>(JsonOpts);
        items!.Should().Contain(i => i.GetProperty("id").GetInt64() == id);
    }

    [Fact(DisplayName = "User role hits the endpoint's role gate (403 only when admin-only)")]
    public async Task UserRole_MatchesRoleGate()
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync(EndpointUrl, EmptyFilterPayload(), JsonOpts);

        AssertUserRoleGate(response);
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        var response = await CreateUnauthenticatedClient().PostAsJsonAsync(EndpointUrl, EmptyFilterPayload(), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}