using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Sydowwe.Framework.Testing.baseTests;

public abstract class BaseGetSelectOptionsEndpointTests(IPostgresFixture fixture)
    : PostgresTestBase(fixture)
{
    // e.g. "/api/leave-type/all-options" — trailing slash optional, framework collapses it.
    protected abstract string EndpointUrl { get; }

    // Seed one entity and return its id; the happy-path test verifies it appears in the options.
    protected abstract Task<long> SeedEntityAsync(DbContext db);

    [Fact]
    public async Task HappyPath_Admin_Returns200WithIdAndText()
    {
        await using var db = CreateDbContext();
        var id = await SeedEntityAsync(db);

        var response = await CreateClient().GetAsync(EndpointUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<JsonElement[]>(JsonOpts);
        items!.Should().Contain(i =>
            i.GetProperty("id").GetInt64() == id &&
            i.GetProperty("text").GetString() != null);
    }

    [Fact(DisplayName = "User role hits the endpoint's role gate (403 only when admin-only)")]
    public async Task UserRole_MatchesRoleGate()
    {
        var response = await CreateUserRoleClient().GetAsync(EndpointUrl);

        AssertUserRoleGate(response);
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        var response = await CreateUnauthenticatedClient().GetAsync(EndpointUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}