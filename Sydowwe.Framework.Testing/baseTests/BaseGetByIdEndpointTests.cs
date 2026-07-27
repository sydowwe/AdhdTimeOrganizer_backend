using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Sydowwe.Framework.Testing.baseTests;

public abstract class BaseGetByIdEndpointTests(IPostgresFixture fixture)
    : PostgresTestBase(fixture)
{
    // e.g. "/api/leave-balance" -- /{id} is appended by the tests
    protected abstract string EndpointUrl { get; }

    // Seed one entity; return its id so the happy-path test can verify it.
    protected abstract Task<long> SeedEntityAsync(DbContext db);

    // Seed an entity owned by a user other than TestUserId, for cross-user IDOR testing.
    // Return null (default) if the endpoint has no user-scoping -- the IDOR test is then skipped.
    protected virtual Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db) => Task.FromResult<long?>(null);

    // What a failed authorization answers. Default 403, matching BaseGetByIdEndpoint. Override to 404 on
    // endpoints that set NotFoundWhenUnauthorized -- i.e. where the row's mere existence is confidential
    // and a 403 would confirm the id is real.
    protected virtual HttpStatusCode UnauthorizedStatus => HttpStatusCode.Forbidden;

    [Fact]
    public async Task HappyPath_Admin_Returns200WithCorrectId()
    {
        await using var db = CreateDbContext();
        var id = await SeedEntityAsync(db);

        var response = await CreateClient().GetAsync($"{EndpointUrl}/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        body.GetProperty("id").GetInt64().Should().Be(id);
    }

    [Fact]
    public async Task NotFound_Returns404()
    {
        var response = await CreateClient().GetAsync($"{EndpointUrl}/99999999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = "User role hits the endpoint's role gate (403 only when admin-only)")]
    public async Task UserRole_MatchesRoleGate()
    {
        await using var db = CreateDbContext();
        var id = await SeedEntityAsync(db);

        var response = await CreateUserRoleClient().GetAsync($"{EndpointUrl}/{id}");

        AssertUserRoleGate(response);
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        await using var db = CreateDbContext();
        var id = await SeedEntityAsync(db);

        var response = await CreateUnauthenticatedClient().GetAsync($"{EndpointUrl}/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "Cross-user read of someone else's row is refused (403, or 404 where existence is confidential)")]
    public async Task CrossUser_WhenNotOwner_IsRefused()
    {
        await using var db = CreateDbContext();
        var id = await SeedEntityOwnedByOtherUserAsync(db);
        if (id is null)
            Assert.Skip("Endpoint has no user-scoping (SeedEntityOwnedByOtherUserAsync returned null).");

        var response = await CreateUserRoleClient().GetAsync($"{EndpointUrl}/{id}");

        response.StatusCode.Should().Be(UnauthorizedStatus);
    }
}