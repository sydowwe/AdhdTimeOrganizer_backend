using System.Net;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.infrastructure.testSupport;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.TestSupport;

/// <summary>
/// The half of the gate <see cref="TestSeedGateTests"/> cannot assert: that a closed gate means the
/// route <b>does not exist</b>, rather than existing and refusing.
///
/// <para>A 403 would confirm the endpoint is there, which is why <c>Program.ConfigureServices</c>
/// filters the type out of FastEndpoints discovery instead of gating it at request time. Nothing about
/// that is visible in a build error or a log line if it regresses — the route simply comes back, wired
/// to a handler that truncates the database — so it is pinned here.</para>
/// </summary>
[Collection("Postgres")]
public class TestSeedRouteRegistrationTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    [Fact]
    public void TheIntegrationHost_HasAnOpenGate()
    {
        // Not a tautology: if this host's environment or connection string ever stops satisfying the
        // gate, every other test in this folder would pass by 404-ing rather than by working.
        var decision = Fixture.UnauthenticatedFactory.Services.GetRequiredService<TestSeedGateDecision>();

        decision.IsOpen.Should().BeTrue(
            "the integration host runs in Development against the Testcontainers database on localhost, " +
            "but the gate says: {0}", decision.Reason);
    }

    /// <summary>
    /// A closed gate must be indistinguishable from a path this app never defined.
    /// <para>
    /// Note what "absent" looks like <i>here</i>: <c>IdentityServiceExtensions</c> sets an authorization
    /// <c>FallbackPolicy</c> of <c>RequireAuthenticatedUser</c>, and ASP.NET's authorization middleware
    /// applies the fallback to requests that match <b>no</b> endpoint as well. So an anonymous caller
    /// gets 401 for every undefined path in this app, not 404 — which is exactly what a closed gate must
    /// also produce. The assertion is therefore "same as an undefined path", not a literal status: what
    /// matters is that the response leaks nothing about the endpoint existing, and in particular is
    /// neither 200 nor 403.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AClosedGate_LeavesTheRouteIndistinguishableFromAnUndefinedPath()
    {
        // Closes the environment half of the gate for one transient host — the deploy-shaped check.
        // An env var rather than the factory's configOverrides on purpose: Program.cs evaluates the
        // gate before builder.Build(), which is where an in-memory override would be applied, but
        // after AddEnvironmentVariables(). The database half cannot be closed this way at all: it
        // takes no configuration, which is the point of it.
        using var env = new EnvVarScope(("ASPNETCORE_ENVIRONMENT", "Production"));

        await using var factory = Fixture.CreateFactory(null);
        using var client = factory.CreateClient();

        var seed = await client.PostAsync("/api/test/seed", null, CancellationToken);
        var undefined = await client.PostAsync("/api/no-such-route-exists", null, CancellationToken);

        seed.StatusCode.Should().Be(undefined.StatusCode);
        seed.StatusCode.Should().NotBe(HttpStatusCode.OK);
        seed.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, "a 403 would confirm the endpoint is there");

        // And the truncation never ran: the baseline fixture user is still there.
        await using var db = (AppDbContext)CreateDbContext();
        (await db.Users.AnyAsync(CancellationToken)).Should().BeTrue();
    }

    [Fact]
    public async Task TheRoute_IsUnderTheApiPrefix_NotAtTheBareRoot()
    {
        using var client = CreateUnauthenticatedClient();

        // /test/seed with no prefix is not a route this app serves — the SPA's global-setup is being
        // changed to match, and a compatibility route was deliberately not added.
        var bare = await client.PostAsync("/test/seed", null, CancellationToken);
        var undefined = await client.PostAsync("/definitely-not-a-route", null, CancellationToken);

        bare.StatusCode.Should().Be(undefined.StatusCode);
        bare.StatusCode.Should().NotBe(HttpStatusCode.OK);

        // ...while the prefixed one is live in this host.
        var prefixed = await client.PostAsync("/api/test/seed", null, CancellationToken);
        prefixed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Sets process env vars for the scope's lifetime and restores them on dispose.</summary>
    private sealed class EnvVarScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originals = new();

        public EnvVarScope(params (string Name, string? Value)[] vars)
        {
            foreach (var (name, value) in vars)
            {
                _originals[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach (var (name, value) in _originals)
                Environment.SetEnvironmentVariable(name, value);
        }
    }
}
