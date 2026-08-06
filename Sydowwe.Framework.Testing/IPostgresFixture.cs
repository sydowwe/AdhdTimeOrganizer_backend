using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Sydowwe.Framework.Testing;

/// <summary>
/// Portal-agnostic contract the test bases depend on, so neither <see cref="PostgresTestBase"/> nor the
/// <c>baseTests</c> endpoint bases need to carry the portal's <c>TProgram</c>/<c>TDbContext</c> type params.
/// The concrete fixture (<see cref="PostgresContainerFixture{TProgram,TDbContext}"/>) implements it; each
/// portal registers a closed subclass as its xUnit collection fixture.
/// </summary>
public interface IPostgresFixture
{
    string ConnectionString { get; }

    /// <summary>A fresh portal DbContext (runtime type is the portal's concrete context).</summary>
    DbContext CreateDbContext();

    /// <summary>Respawn the database back to the seeded baseline (called per-test).</summary>
    Task ResetAsync();

    ITestClientFactory AdminAndUserFactory { get; }
    ITestClientFactory AdminFactory { get; }
    ITestClientFactory UserFactory { get; }
    ITestClientFactory RootFactory { get; }
    ITestClientFactory UnauthenticatedFactory { get; }

    /// <summary>
    /// Build an ad-hoc factory for a custom role/userId combo, optionally overriding host services
    /// (e.g. swapping <c>IWordTemplateService</c> or a command handler for a fake) and/or pinning
    /// configuration values (e.g. an endpoint feature toggle) via an in-memory source. Caller disposes.
    /// </summary>
    ITestClientFactory CreateFactory(string[]? roles, long? userId = null,
        Action<IServiceCollection>? configureServices = null,
        IEnumerable<KeyValuePair<string, string?>>? configOverrides = null,
        bool preserveExecutionContext = false);
}