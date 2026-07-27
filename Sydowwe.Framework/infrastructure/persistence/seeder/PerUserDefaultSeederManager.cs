using Microsoft.Extensions.Logging;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface.manager;

namespace Sydowwe.Framework.infrastructure.persistence.seeder;

/// <summary>
/// Runs per-user production seeders. Exceptions propagate — on the sign-up path the caller has to
/// know the new account did not get its baseline rows.
/// </summary>
public class PerUserDefaultSeederManager(
    IServiceProvider serviceProvider,
    ISeedUserProvider seedUsers,
    ILogger<PerUserDefaultSeederManager> logger) : IScopedService, IPerUserDefaultSeederManager
{
    public async Task SeedAllForUserAsync(long userId, bool overrideData = false, CancellationToken ct = default)
    {
        var seeders = SeederResolver.Resolve<IPerUserDefaultSeeder>(serviceProvider);

        foreach (var seeder in seeders)
            await SeedOneAsync(seeder, userId, overrideData, ct);

        logger.LogInformation("Completed {Count} per-user default seeders for user {UserId}", seeders.Count, userId);
    }

    public async Task SeedAllForAllUsersAsync(bool overrideData = false, CancellationToken ct = default)
    {
        var userIds = await seedUsers.GetAllUserIdsAsync(ct);

        if (userIds.Count == 0)
        {
            logger.LogWarning("No users found. Skipping per-user default seeding.");
            return;
        }

        foreach (var userId in userIds)
            await SeedAllForUserAsync(userId, overrideData, ct);

        logger.LogInformation("Completed per-user defaults for {UserCount} users", userIds.Count);
    }

    public Task SeedForUserAsync(string seederName, long userId, bool overrideData = false, CancellationToken ct = default)
    {
        var seeder = SeederResolver.ResolveByName<IPerUserDefaultSeeder>(serviceProvider, seederName);
        return SeedOneAsync(seeder, userId, overrideData, ct);
    }

    /// <summary>
    /// Reset-then-setup. <c>SetupDefaults</c> runs unconditionally because it is an upsert that
    /// inserts only what is missing — after a successful reset it finds nothing to do, and after a
    /// reset that bailed out (the user's rows don't line up with the defaults) it is what fills the
    /// gaps. Both steps have to be idempotent; that is the contract on
    /// <see cref="IPerUserDefaultSeeder"/>.
    /// </summary>
    private async Task SeedOneAsync(IPerUserDefaultSeeder seeder, long userId, bool overrideData, CancellationToken ct)
    {
        if (overrideData)
        {
            logger.LogInformation("Resetting {SeederName} for user {UserId}", seeder.SeederName, userId);
            var wasReset = await seeder.ResetDefaults(userId, ct);

            if (!wasReset)
                logger.LogInformation(
                    "{SeederName} could not be reset in place for user {UserId} — falling back to inserting what is missing",
                    seeder.SeederName, userId);
        }

        logger.LogInformation("Seeding {SeederName} for user {UserId}", seeder.SeederName, userId);
        await seeder.SetupDefaults(userId, ct);
    }

    public IEnumerable<string> GetAllSeederNames() =>
        SeederResolver.Resolve<IPerUserDefaultSeeder>(serviceProvider).Select(s => s.SeederName);
}
