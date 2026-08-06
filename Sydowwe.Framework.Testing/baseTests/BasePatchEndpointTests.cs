using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Sydowwe.Framework.Testing.baseTests;

public abstract class BasePatchEndpointTests(IPostgresFixture fixture)
    : PostgresTestBase(fixture)
{
    // e.g. "/api/complaint" — /{id} is appended by the tests
    protected abstract string EndpointUrl { get; }

    // Seed one entity and return its id.
    protected abstract Task<long> SeedEntityAsync(DbContext db);

    // Build a valid patch payload for the given id.
    protected abstract Task<object> BuildValidPayloadAsync(DbContext db, long id);

    // Override to seed an entity the default CreateClient() user must NOT be allowed to patch
    // (e.g. owned by a different user) for endpoints that override BasePatchEndpoint.AuthorizeAsync.
    // Return null (default) if this endpoint has no ownership scoping — the IDOR test is then skipped.
    protected virtual Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db) => Task.FromResult<long?>(null);

    [Fact]
    public async Task HappyPath_Admin_Returns204()
    {
        await using var db = CreateDbContext();
        var id = await SeedEntityAsync(db);
        var payload = await BuildValidPayloadAsync(db, id);

        var response = await CreateClient().PatchAsJsonAsync($"{EndpointUrl}/{id}", payload, JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task NonExistentId_Returns404()
    {
        await using var db = CreateDbContext();
        var id = await SeedEntityAsync(db);
        var payload = await BuildValidPayloadAsync(db, id);

        var response = await CreateClient().PatchAsJsonAsync($"{EndpointUrl}/99999999", payload, JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = "User role hits the endpoint's role gate (403 only when admin-only)")]
    public async Task UserRole_MatchesRoleGate()
    {
        await using var db = CreateDbContext();
        var id = await SeedEntityAsync(db);
        var payload = await BuildValidPayloadAsync(db, id);

        var response = await CreateUserRoleClient().PatchAsJsonAsync($"{EndpointUrl}/{id}", payload, JsonOpts);

        AssertUserRoleGate(response);
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        await using var db = CreateDbContext();
        var id = await SeedEntityAsync(db);
        var payload = await BuildValidPayloadAsync(db, id);

        var response = await CreateUnauthenticatedClient().PatchAsJsonAsync($"{EndpointUrl}/{id}", payload, JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task NotOwner_Returns403()
    {
        await using var db = CreateDbContext();
        var id = await SeedEntityOwnedByOtherUserAsync(db);
        if (id is null)
            Assert.Skip("Endpoint has no ownership scoping (SeedEntityOwnedByOtherUserAsync returned null).");

        var payload = await BuildValidPayloadAsync(db, id.Value);

        var response = await CreateClient().PatchAsJsonAsync($"{EndpointUrl}/{id}", payload, JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}