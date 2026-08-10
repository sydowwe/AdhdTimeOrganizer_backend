using System.Net;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// The TodoLists counterpart of <see cref="CoreRouteSmokeTests"/>. Same reasoning: the slice's
/// endpoints only route if <c>AdhdTimeOrganizer.TodoLists</c> is in the FastEndpoints
/// <c>o.Assemblies</c> list in <c>Program.cs</c> (<c>DisableAutoDiscovery = true</c>), and leaving it
/// out is not a build error — every to-do route simply 404s.
/// <para>
/// Seeder double-registration is already covered globally by
/// <c>CoreRouteSmokeTests.CoreSeeders_AreRegisteredExactlyOnce</c>, which asserts over every
/// registered seeder rather than Core's alone, so this slice needs no separate copy of it.
/// </para>
/// </summary>
[Collection("Postgres")]
public class TodoListsRouteSmokeTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    /// <summary>
    /// One route per family that moved into the slice: the list, the item, and the priority lookup —
    /// the last one because <c>TaskPriority</c> was filed under the Planning folder while belonging
    /// here, so it is the entity most likely to be left behind by a folder-driven move.
    /// </summary>
    [Theory]
    [InlineData("/api/todo-list/all-options")]
    [InlineData("/api/todo-list-item")]
    [InlineData("/api/task-priority")]
    public async Task TodoListRoutes_AreRegistered(string route)
    {
        var client = CreateUserRoleClient();

        var response = await client.GetAsync(route, TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
    }
}
