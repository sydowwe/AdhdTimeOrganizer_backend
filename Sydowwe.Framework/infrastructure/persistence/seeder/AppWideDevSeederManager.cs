using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface.manager;

namespace Sydowwe.Framework.infrastructure.persistence.seeder;

/// <summary>
/// Runs app-wide dev fixtures. Unlike the default managers this logs and continues on failure: one
/// broken fixture should not cost a developer the other twenty, and nothing in production depends on
/// the outcome. Call it only in development — it truncates.
/// </summary>
public class AppWideDevSeederManager(
    IServiceProvider serviceProvider,
    DbContext dbContext,
    ILogger<AppWideDevSeederManager> logger) : IScopedService, IAppWideDevSeederManager
{
    public Task SeedAllAsync(bool overrideData = true) => SeedAsync(assembly: null, overrideData);

    public Task SeedAssemblyAsync(Assembly assembly, bool overrideData = true) => SeedAsync(assembly, overrideData);

    private async Task SeedAsync(Assembly? assembly, bool overrideData)
    {
        if (overrideData)
            await TruncateAsync(assembly);

        var seeders = SeederResolver.Resolve<IAppWideDevSeeder>(serviceProvider, assembly);

        foreach (var seeder in seeders)
            try
            {
                logger.LogInformation("Seeding app-wide dev data: {SeederName}", seeder.SeederName);
                await seeder.Seed();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error occurred while seeding app-wide dev data {SeederName}", seeder.SeederName);
            }

        logger.LogInformation("Completed {Count} app-wide dev seeders.", seeders.Count);
    }

    public Task SeedAsync(string seederName)
    {
        var seeder = SeederResolver.ResolveByName<IAppWideDevSeeder>(serviceProvider, seederName);

        logger.LogInformation("Seeding app-wide dev data: {SeederName}", seeder.SeederName);
        return seeder.Seed();
    }

    public Task TruncateAllTablesAsync() => TruncateAsync(assembly: null);

    private async Task TruncateAsync(Assembly? assembly)
    {
        foreach (var seeder in SeederResolver.ResolveForTruncation<IAppWideDevSeeder>(serviceProvider, assembly))
            await seeder.TruncateTable();

        // TRUNCATE happens in the database, behind the ChangeTracker's back — anything already tracked
        // now describes rows that no longer exist, and the next SaveChanges would try to update them.
        dbContext.ChangeTracker.Clear();
    }

    public IEnumerable<string> GetAllSeederNames() =>
        SeederResolver.Resolve<IAppWideDevSeeder>(serviceProvider).Select(s => s.SeederName);
}