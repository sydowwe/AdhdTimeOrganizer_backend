using Microsoft.Extensions.Logging;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface.manager;

namespace Sydowwe.Framework.infrastructure.persistence.seeder;

/// <summary>
/// Runs app-wide production seeders. Exceptions propagate: unlike a dev fixture, a default that
/// failed to seed leaves the app in a state where roles or the admin account may be missing, and
/// that must not be a log line the caller can miss.
/// </summary>
public class AppWideDefaultSeederManager(IServiceProvider serviceProvider, ILogger<AppWideDefaultSeederManager> logger)
    : IScopedService, IAppWideDefaultSeederManager
{
    public async Task SeedAllAsync(bool overrideData = false)
    {
        var seeders = SeederResolver.Resolve<IAppWideDefaultSeeder>(serviceProvider);

        foreach (var seeder in seeders)
        {
            logger.LogInformation("Seeding app-wide defaults: {SeederName}", seeder.SeederName);
            await seeder.Seed(overrideData);
        }

        logger.LogInformation("Completed {Count} app-wide default seeders.", seeders.Count);
    }

    public Task SeedAsync(string seederName, bool overrideData = false)
    {
        var seeder = SeederResolver.ResolveByName<IAppWideDefaultSeeder>(serviceProvider, seederName);

        logger.LogInformation("Seeding app-wide defaults: {SeederName}", seeder.SeederName);
        return seeder.Seed(overrideData);
    }

    public IEnumerable<string> GetAllSeederNames() =>
        SeederResolver.Resolve<IAppWideDefaultSeeder>(serviceProvider).Select(s => s.SeederName);
}