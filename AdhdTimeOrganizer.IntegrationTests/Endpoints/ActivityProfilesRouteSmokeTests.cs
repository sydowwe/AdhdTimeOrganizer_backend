using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// Pins the host-side registrations AdhdTimeOrganizer.ActivityProfiles needs, plus the dependency
/// direction that made the slice possible. Every failure mode here is silent — nothing fails to
/// build, and in two of the three cases nothing throws or logs either.
/// </summary>
[Collection("Postgres")]
public class ActivityProfilesRouteSmokeTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    /// <summary>
    /// The slice's 52 endpoints only route if its assembly is in the FastEndpoints
    /// <c>o.Assemblies</c> list in <c>Program.cs</c> (<c>DisableAutoDiscovery = true</c>). Leaving it
    /// out is not a build error — all 52 simply 404. One route per endpoint family, because the
    /// families reach the router three different ways: the profile endpoints declare explicit routes,
    /// the lookup and anchor grids get theirs from <c>BaseGridEndpoint</c>'s
    /// <c>EntityName.Kebaberize()</c> convention.
    /// </summary>
    [Theory]
    [InlineData("/api/activity-backlog-profile/all-options")]
    [InlineData("/api/activity-bucket-list-profile/all-options")]
    [InlineData("/api/activity-project-profile/all-options")]
    public async Task ProfileRoutes_AreRegistered(string route)
    {
        var response = await CreateUserRoleClient().GetAsync(route, TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The four activity lookups and MemoryAnchor route through <c>BaseGridEndpoint</c>, so their
    /// paths are derived rather than written down — a rename of the entity silently moves the route.
    /// </summary>
    [Theory]
    [InlineData("/api/activity-location-type/filtered-table")]
    [InlineData("/api/activity-weather-dependency/filtered-table")]
    [InlineData("/api/activity-expected-cost-tier/filtered-table")]
    [InlineData("/api/activity-experience-type/filtered-table")]
    [InlineData("/api/memory-anchor/filtered-table")]
    public async Task LookupAndAnchorGridRoutes_AreRegistered(string route)
    {
        var response = await CreateUserRoleClient()
            .PostAsJsonAsync(route, new { ItemsPerPage = 10, Page = 1 }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// This slice carries more seeders than any other — four per-user default seeders (the activity
    /// lookups) and eight dev seeders. They carry the framework lifetime markers, so the assembly must
    /// be scanned by exactly one of the two scans: <c>ModuleServiceExtensions.ModuleAssemblies</c>
    /// (which <c>AddDependencyInjection</c> <c>Except</c>s) or the <c>AppDomain</c> sweep. Being in
    /// both doubles every <c>IEnumerable&lt;T&gt;</c>: the dev seeders truncate and reseed twice per
    /// run, and the per-user defaults insert every default twice on sign-up, against the very unique
    /// indexes <c>Collides</c> exists to respect. Nothing throws or logs.
    /// </summary>
    [Fact]
    public void ActivityProfilesSeeders_AreRegisteredExactlyOnce()
    {
        using var scope = Fixture.AdminAndUserFactory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        AssertNoDuplicates(sp.GetServices<IPerUserDefaultSeeder>().Select(s => s.SeederName));
        AssertNoDuplicates(sp.GetServices<IPerUserDevSeeder>().Select(s => s.SeederName));
        AssertNoDuplicates(sp.GetServices<IAppWideDevSeeder>().Select(s => s.SeederName));
    }

    /// <summary>
    /// The whole point of the slice: Core must not reference it. Core owns <c>Activity</c>, and this
    /// project's eight entities all hang off <c>Activity</c> — so the tempting fix for any awkward
    /// query is to put a navigation back on <c>Activity</c> (<c>BacklogProfile</c>,
    /// <c>ProjectProfile</c>, <c>BucketListProfile</c>, <c>MemoryAnchors</c> all used to exist). Doing
    /// so requires a project reference, which inverts the dependency direction for every slice, since
    /// they all sit on Core. It would compile fine — hence this assertion.
    /// </summary>
    [Fact]
    public void Core_DoesNotReferenceActivityProfiles()
    {
        var coreReferences = typeof(Activity).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        coreReferences.Should().NotContain(typeof(ActivityBacklogProfile).Assembly.GetName().Name);
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
