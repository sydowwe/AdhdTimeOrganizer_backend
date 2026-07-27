using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Sydowwe.Framework.Testing.baseTests;

public abstract class BaseCreateEndpointTests(IPostgresFixture fixture)
    : PostgresTestBase(fixture)
{
    // e.g. "/api/stock-type"
    protected abstract string EndpointUrl { get; }

    // Build a valid create payload. db is available for seeding any FK dependencies first.
    protected abstract Task<object> BuildValidPayloadAsync(DbContext db);

    [Fact]
    public async Task HappyPath_Admin_Returns201WithId()
    {
        await using var db = CreateDbContext();
        var payload = await BuildValidPayloadAsync(db);

        var response = await CreateClient().PostAsJsonAsync(EndpointUrl, payload, JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await response.Content.ReadFromJsonAsync<long>(TestContext.Current.CancellationToken);
        id.Should().BeGreaterThan(0);
    }

    [Fact(DisplayName = "User role hits the endpoint's role gate (403 only when admin-only)")]
    public async Task UserRole_MatchesRoleGate()
    {
        await using var db = CreateDbContext();
        var payload = await BuildValidPayloadAsync(db);

        var response = await CreateUserRoleClient().PostAsJsonAsync(EndpointUrl, payload, JsonOpts);

        AssertUserRoleGate(response);
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        await using var db = CreateDbContext();
        var payload = await BuildValidPayloadAsync(db);

        var response = await CreateUnauthenticatedClient().PostAsJsonAsync(EndpointUrl, payload, JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}