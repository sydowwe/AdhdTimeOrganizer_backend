using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Sydowwe.Framework.Testing.baseTests;

public abstract class BaseDeleteEndpointTests(IPostgresFixture fixture)
    : PostgresTestBase(fixture)
{
    // e.g. "/api/stock-type" â€” /{id} is appended by the tests
    protected abstract string EndpointUrl { get; }

    // Seed one entity; return its id.
    protected abstract Task<long> SeedEntityAsync(DbContext db);

    // Override to seed an entity the default CreateClient() user must NOT be allowed to delete
    // (e.g. owned by a different user) for endpoints that override BaseDeleteEndpoint.AuthorizeAsync.
    // Return null (default) if this endpoint has no ownership scoping — the IDOR test is then skipped.
    protected virtual Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db) => Task.FromResult<long?>(null);

    [Fact]
    public async Task HappyPath_Admin_Returns204()
    {
        await using var db = CreateDbContext();
        var id = await SeedEntityAsync(db);

        var response = await CreateClient().DeleteAsync($"{EndpointUrl}/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task NonExistentId_Returns404()
    {
        var response = await CreateClient().DeleteAsync($"{EndpointUrl}/99999999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = "User role hits the endpoint's role gate (403 only when admin-only)")]
    public async Task UserRole_MatchesRoleGate()
    {
        await using var db = CreateDbContext();
        var id = await SeedEntityAsync(db);

        var response = await CreateUserRoleClient().DeleteAsync($"{EndpointUrl}/{id}");

        AssertUserRoleGate(response);
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        await using var db = CreateDbContext();
        var id = await SeedEntityAsync(db);

        var response = await CreateUnauthenticatedClient().DeleteAsync($"{EndpointUrl}/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task NotOwner_Returns403()
    {
        await using var db = CreateDbContext();
        var id = await SeedEntityOwnedByOtherUserAsync(db);
        if (id is null)
            Assert.Skip("Endpoint has no ownership scoping (SeedEntityOwnedByOtherUserAsync returned null).");

        var response = await CreateClient().DeleteAsync($"{EndpointUrl}/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}