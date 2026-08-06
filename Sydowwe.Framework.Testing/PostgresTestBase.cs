using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Sydowwe.Framework.Testing;

/// <summary>
/// Portal-agnostic test base. Depends only on <see cref="IPostgresFixture"/>, so portals just subclass it
/// (passing their closed collection fixture) without re-threading any type parameters.
/// </summary>
public abstract class PostgresTestBase(IPostgresFixture fixture) : IAsyncLifetime
{
    protected static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    protected static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // Prevent the runtime from following the auth-challenge redirect — tests need to observe the 401/403
    // directly. Used for the role-restricted and unauthenticated clients below.
    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };

    protected IPostgresFixture Fixture => fixture;
    protected string ConnectionString => fixture.ConnectionString;

    // Default client: authenticated as Admin + User. Use this for the common "happy path".
    protected HttpClient CreateClient() => fixture.AdminAndUserFactory.CreateClient();

    // Role-scoped clients: each authenticates as exactly one role, for testing role gates.
    protected HttpClient CreateAdminRoleClient() => fixture.AdminFactory.CreateClient(NoRedirect);

    protected HttpClient CreateUserRoleClient() => fixture.UserFactory.CreateClient(NoRedirect);

    protected HttpClient CreateRootRoleClient() => fixture.RootFactory.CreateClient(NoRedirect);

    // No auth handler registered → real auth layer challenges anonymous requests with 401.
    protected HttpClient CreateUnauthenticatedClient() => fixture.UnauthenticatedFactory.CreateClient(NoRedirect);

    // For tests that need a different test user id (cross-employee IDOR / ownership checks) or that
    // override host services (e.g. faking IWordTemplateService / a command handler via configureServices).
    // Caller owns the returned factory + client and must dispose them.
    protected ITestClientFactory CreateFactory(string[]? roles, long? userId = null,
        Action<IServiceCollection>? configureServices = null,
        IEnumerable<KeyValuePair<string, string?>>? configOverrides = null,
        bool preserveExecutionContext = false) =>
        fixture.CreateFactory(roles, userId, configureServices, configOverrides, preserveExecutionContext);

    protected DbContext CreateDbContext() => fixture.CreateDbContext();

    // The base endpoints allow User + Admin + Root. Override to true in a test whose endpoint narrows
    // AllowedRoles() back to Admin + Root, and the shared role-gate guard flips to expecting a 403.
    protected virtual bool IsAdminOnly => false;

    /// <summary>
    /// Asserts the role gate for a request made with the plain User-role client: refused when the
    /// endpoint is admin-only, otherwise merely "not refused for being a User" (the concrete status
    /// depends on the verb and the seeded row, which each endpoint's own happy-path test covers).
    /// </summary>
    protected void AssertUserRoleGate(HttpResponseMessage response)
    {
        if (IsAdminOnly)
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        else
            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateDbContext();
        await SeedAsync(db);
    }

    protected virtual Task SeedAsync(DbContext db) => Task.CompletedTask;

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}