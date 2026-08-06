using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Sydowwe.Framework.Testing.baseTests;

public abstract class BaseToggleIsHiddenEndpointTests(IPostgresFixture fixture)
    : PostgresTestBase(fixture)
{
    // e.g. "/api/complaint/toggle-is-hidden"
    protected abstract string EndpointUrl { get; }

    // Seed one entity (default IsHidden=false) and return its id.
    protected abstract Task<long> SeedEntityAsync(DbContext db);

    // Override to seed an entity the default CreateClient() user must NOT be allowed to toggle
    // (e.g. owned by a different user) for endpoints that override BaseToggleIsHiddenEndpoint.AuthorizeAsync.
    // Return null (default) if this endpoint has no ownership scoping — the IDOR test is then skipped.
    protected virtual Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db) => Task.FromResult<long?>(null);

    private static object Body(IEnumerable<long> ids) => new { ids };

    [Fact]
    public async Task HappyPath_Admin_Returns204()
    {
        await using var db = CreateDbContext();
        var id = await SeedEntityAsync(db);

        var response = await CreateClient().PatchAsJsonAsync(EndpointUrl, Body([id]), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task NonExistentId_Returns404()
    {
        var response = await CreateClient().PatchAsJsonAsync(EndpointUrl, Body([99999999L]), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = "User role hits the endpoint's role gate (403 only when admin-only)")]
    public async Task UserRole_MatchesRoleGate()
    {
        await using var db = CreateDbContext();
        var id = await SeedEntityAsync(db);

        var response = await CreateUserRoleClient().PatchAsJsonAsync(EndpointUrl, Body([id]), JsonOpts);

        AssertUserRoleGate(response);
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        await using var db = CreateDbContext();
        var id = await SeedEntityAsync(db);

        var response = await CreateUnauthenticatedClient().PatchAsJsonAsync(EndpointUrl, Body([id]), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task NotOwner_Returns403()
    {
        await using var db = CreateDbContext();
        var id = await SeedEntityOwnedByOtherUserAsync(db);
        if (id is null)
            Assert.Skip("Endpoint has no ownership scoping (SeedEntityOwnedByOtherUserAsync returned null).");

        var response = await CreateClient().PatchAsJsonAsync(EndpointUrl, Body([id.Value]), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}