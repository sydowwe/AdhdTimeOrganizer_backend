using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface.manager;

namespace Sydowwe.Framework.infrastructure.persistence.seeder;

/// <summary>
/// Runs per-user dev fixtures. Logs and continues on failure, same reasoning as
/// <see cref="AppWideDevSeederManager"/>. Call it only in development — it truncates.
/// </summary>
public class PerUserDevSeederManager(
    IServiceProvider serviceProvider,
    DbContext dbContext,
    ISeedUserProvider seedUsers,
    ILogger<PerUserDevSeederManager> logger) : IScopedService, IPerUserDevSeederManager
{
    public Task SeedAllForUserAsync(long userId, bool overrideData = true) =>
        SeedForUserAsync(assembly: null, userId, overrideData);

    public Task SeedAssemblyForUserAsync(Assembly assembly, long userId, bool overrideData = true) =>
        SeedForUserAsync(assembly, userId, overrideData);

    public async Task SeedAllForRootAdminAsync(bool overrideData = true)
    {
        var rootAdminUserId = await seedUsers.GetRootAdminUserIdAsync();

        if (!rootAdminUserId.HasValue)
        {
            // Expected on a fresh database seeded in one pass: the app-wide default seeders create the
            // root admin, so if they have not run yet there is simply nobody to hang fixtures off.
            logger.LogWarning("Root admin user not found. Skipping per-user dev seeding.");
            return;
        }

        await SeedAllForUserAsync(rootAdminUserId.Value, overrideData);
    }

    private async Task SeedForUserAsync(Assembly? assembly, long userId, bool overrideData)
    {
        if (overrideData)
            await TruncateAsync(assembly);

        var seeders = SeederResolver.Resolve<IPerUserDevSeeder>(serviceProvider, assembly);

        foreach (var seeder in seeders)
            try
            {
                logger.LogInformation("Seeding dev data: {SeederName} for user {UserId}", seeder.SeederName, userId);
                await seeder.SeedForUser(userId);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error occurred while seeding dev data {SeederName} for user {UserId}",
                    seeder.SeederName, userId);
            }

        logger.LogInformation("Completed {Count} per-user dev seeders for user {UserId}", seeders.Count, userId);
    }

    public Task SeedForUserAsync(string seederName, long userId)
    {
        var seeder = SeederResolver.ResolveByName<IPerUserDevSeeder>(serviceProvider, seederName);

        logger.LogInformation("Seeding dev data: {SeederName} for user {UserId}", seeder.SeederName, userId);
        return seeder.SeedForUser(userId);
    }

    public Task TruncateAllTablesAsync() => TruncateAsync(assembly: null);

    private async Task TruncateAsync(Assembly? assembly)
    {
        foreach (var seeder in SeederResolver.ResolveForTruncation<IPerUserDevSeeder>(serviceProvider, assembly))
            await seeder.TruncateTable();

        // TRUNCATE happens in the database, behind the ChangeTracker's back — anything already tracked
        // now describes rows that no longer exist, and the next SaveChanges would try to update them.
        dbContext.ChangeTracker.Clear();
    }

    public IEnumerable<string> GetAllSeederNames() =>
        SeederResolver.Resolve<IPerUserDevSeeder>(serviceProvider).Select(s => s.SeederName);
}
