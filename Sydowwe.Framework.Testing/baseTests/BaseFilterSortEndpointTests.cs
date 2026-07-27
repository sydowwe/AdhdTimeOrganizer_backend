using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Sydowwe.Framework.Testing.baseTests;

public abstract class BaseFilterSortEndpointTests(IPostgresFixture fixture)
    : PostgresTestBase(fixture)
{
    // e.g. "/api/complaint/filter-sort"
    protected abstract string EndpointUrl { get; }

    // Seed one entity and return its id.
    protected abstract Task<long> SeedEntityAsync(DbContext db);

    // BaseFilterSortRequest<TFilter> shape: { useFilter, filter, sortBy[] }
    protected virtual object FilterSortPayload(bool useFilter = false, object? filter = null) =>
        new
        {
            useFilter,
            filter = filter ?? new { },
            sortBy = Array.Empty<object>()
        };

    [Fact]
    public async Task HappyPath_Admin_Returns200WithSeededEntity()
    {
        await using var db = CreateDbContext();
        var id = await SeedEntityAsync(db);

        var response = await CreateClient().PostAsJsonAsync(EndpointUrl, FilterSortPayload(), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<JsonElement[]>(JsonOpts);
        items!.Should().Contain(i => i.GetProperty("id").GetInt64() == id);
    }

    [Fact(DisplayName = "User role hits the endpoint's role gate (403 only when admin-only)")]
    public async Task UserRole_MatchesRoleGate()
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync(EndpointUrl, FilterSortPayload(), JsonOpts);

        AssertUserRoleGate(response);
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        var response = await CreateUnauthenticatedClient().PostAsJsonAsync(EndpointUrl, FilterSortPayload(), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}