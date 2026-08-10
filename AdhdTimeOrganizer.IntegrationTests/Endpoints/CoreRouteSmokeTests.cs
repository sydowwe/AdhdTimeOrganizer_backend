using System.Net;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.Testing;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// Pins the two host-side registrations that AdhdTimeOrganizer.Core needs and that no compiler
/// checks. Both failure modes are silent, which is the whole reason this suite exists.
/// </summary>
[Collection("Postgres")]
public class CoreRouteSmokeTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    /// <summary>
    /// Core's endpoints only route if its assembly is in the FastEndpoints <c>o.Assemblies</c> list in
    /// <c>Program.cs</c> (<c>DisableAutoDiscovery = true</c>). Leaving it out is not a build error —
    /// every activity and timer route simply 404s. One route from each family is enough to catch that.
    /// </summary>
    [Fact]
    public async Task ActivityAndTimerRoutes_AreRegistered()
    {
        var client = CreateUserRoleClient();

        var activity = await client.GetAsync("/api/activity", TestContext.Current.CancellationToken);
        var timer = await client.GetAsync("/api/timer-preset", TestContext.Current.CancellationToken);

        activity.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
        timer.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Core carries the framework lifetime markers, so it must be scanned by exactly one of the two
    /// scans: <c>ModuleServiceExtensions.ModuleAssemblies</c> (which <c>AddDependencyInjection</c>
    /// <c>Except</c>s) or the <c>AppDomain</c> sweep. In both, single resolutions still work — but every
    /// <c>IEnumerable&lt;T&gt;</c> doubles, and each per-user default seeder would run twice against the
    /// same unique indexes. Nothing throws or logs; the duplicate count is the only visible symptom.
    /// </summary>
    [Fact]
    public void CoreSeeders_AreRegisteredExactlyOnce()
    {
        using var scope = Fixture.AdminAndUserFactory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        AssertNoDuplicates(sp.GetServices<IPerUserDefaultSeeder>().Select(s => s.SeederName));
        AssertNoDuplicates(sp.GetServices<IPerUserDevSeeder>().Select(s => s.SeederName));
        AssertNoDuplicates(sp.GetServices<IAppWideDefaultSeeder>().Select(s => s.SeederName));
    }

    private static void AssertNoDuplicates(IEnumerable<string> seederNames)
    {
        var duplicated = seederNames
            .GroupBy(n => n)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} x{g.Count()}")
            .ToList();

        duplicated.Should().BeEmpty();
    }
}
