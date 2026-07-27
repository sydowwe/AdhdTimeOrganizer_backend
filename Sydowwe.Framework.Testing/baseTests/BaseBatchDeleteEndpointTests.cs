using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Sydowwe.Framework.Testing.baseTests;

public abstract class BaseBatchDeleteEndpointTests(IPostgresFixture fixture)
    : PostgresTestBase(fixture)
{
    // e.g. "/api/complaint/batch-delete"
    protected abstract string EndpointUrl { get; }

    // Seed N entities and return their ids.
    protected abstract Task<long[]> SeedEntitiesAsync(DbContext db, int count);

    // Override to seed an entity the default CreateClient() user must NOT be allowed to delete
    // (e.g. owned by a different user) for endpoints that override BaseBatchDeleteEndpoint.AuthorizeAsync.
    // Return null (default) if this endpoint has no ownership scoping — the IDOR test is then skipped.
    protected virtual Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db) => Task.FromResult<long?>(null);

    private static object Body(IEnumerable<long> ids) => new { ids };

    [Fact]
    public async Task HappyPath_Admin_Returns204()
    {
        await using var db = CreateDbContext();
        var ids = await SeedEntitiesAsync(db, 2);

        var response = await CreateClient().PostAsJsonAsync(EndpointUrl, Body(ids), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task EmptyIds_Returns400()
    {
        var response = await CreateClient().PostAsJsonAsync(EndpointUrl, Body([]), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task NonExistentId_Returns404()
    {
        var response = await CreateClient().PostAsJsonAsync(EndpointUrl, Body([99999999L]), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = "User role hits the endpoint's role gate (403 only when admin-only)")]
    public async Task UserRole_MatchesRoleGate()
    {
        await using var db = CreateDbContext();
        var ids = await SeedEntitiesAsync(db, 1);

        var response = await CreateUserRoleClient().PostAsJsonAsync(EndpointUrl, Body(ids), JsonOpts);

        AssertUserRoleGate(response);
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        await using var db = CreateDbContext();
        var ids = await SeedEntitiesAsync(db, 1);

        var response = await CreateUnauthenticatedClient().PostAsJsonAsync(EndpointUrl, Body(ids), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task NotOwner_Returns403()
    {
        await using var db = CreateDbContext();
        var id = await SeedEntityOwnedByOtherUserAsync(db);
        if (id is null)
            Assert.Skip("Endpoint has no ownership scoping (SeedEntityOwnedByOtherUserAsync returned null).");

        var response = await CreateClient().PostAsJsonAsync(EndpointUrl, Body([id.Value]), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}