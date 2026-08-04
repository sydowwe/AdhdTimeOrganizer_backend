using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Sydowwe.Framework.Testing.baseTests;

public abstract class BaseUpdateEndpointTests(IPostgresFixture fixture)
    : PostgresTestBase(fixture)
{
    // e.g. "/api/stock-type" â€” /{id} is appended by the tests
    protected abstract string EndpointUrl { get; }

    // Seed one entity; return its id.
    protected abstract Task<long> SeedEntityAsync(DbContext db);

    // Build a valid full-update payload for the given id.
    protected abstract Task<object> BuildValidPayloadAsync(DbContext db, long id);

    // Override to seed an entity the default CreateClient() user must NOT be allowed to update
    // (e.g. owned by a different user) for endpoints that override BaseUpdateEndpoint.AuthorizeAsync.
    // Return null (default) if this endpoint has no ownership scoping — the IDOR test is then skipped.
    protected virtual Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db) => Task.FromResult<long?>(null);

    // What a refused cross-user update answers. Default 403, matching BaseUpdateEndpoint.AuthorizeAsync.
    // Override to 404 for entities carrying a global user query filter: the row is filtered out of the
    // lookup entirely, so the endpoint answers "not found" and the authorization hook never runs. Same
    // hook, same reasoning as BaseGetByIdEndpointTests.UnauthorizedStatus.
    protected virtual HttpStatusCode UnauthorizedStatus => HttpStatusCode.Forbidden;

    [Fact]
    public async Task HappyPath_Admin_Returns200()
    {
        await using var db = CreateDbContext();
        var id = await SeedEntityAsync(db);
        var payload = await BuildValidPayloadAsync(db, id);

        var response = await CreateClient().PutAsJsonAsync($"{EndpointUrl}/{id}", payload, JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task NonExistentId_Returns404()
    {
        await using var db = CreateDbContext();
        var id = await SeedEntityAsync(db);
        var payload = await BuildValidPayloadAsync(db, id);

        var response = await CreateClient().PutAsJsonAsync($"{EndpointUrl}/99999999", payload, JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = "User role hits the endpoint's role gate (403 only when admin-only)")]
    public async Task UserRole_MatchesRoleGate()
    {
        await using var db = CreateDbContext();
        var id = await SeedEntityAsync(db);
        var payload = await BuildValidPayloadAsync(db, id);

        var response = await CreateUserRoleClient().PutAsJsonAsync($"{EndpointUrl}/{id}", payload, JsonOpts);

        AssertUserRoleGate(response);
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        await using var db = CreateDbContext();
        var id = await SeedEntityAsync(db);
        var payload = await BuildValidPayloadAsync(db, id);

        var response = await CreateUnauthenticatedClient().PutAsJsonAsync($"{EndpointUrl}/{id}", payload, JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "Cross-user update of someone else's row is refused (403, or 404 where a query filter hides it)")]
    public async Task NotOwner_IsRefused()
    {
        await using var db = CreateDbContext();
        var id = await SeedEntityOwnedByOtherUserAsync(db);
        if (id is null)
            Assert.Skip("Endpoint has no ownership scoping (SeedEntityOwnedByOtherUserAsync returned null).");

        var payload = await BuildValidPayloadAsync(db, id.Value);

        var response = await CreateClient().PutAsJsonAsync($"{EndpointUrl}/{id}", payload, JsonOpts);

        response.StatusCode.Should().Be(UnauthorizedStatus);
    }
}