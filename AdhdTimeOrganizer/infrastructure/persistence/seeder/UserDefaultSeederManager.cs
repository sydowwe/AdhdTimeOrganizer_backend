using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface.manager;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder;

public class UserDefaultSeederManager(
    IServiceProvider serviceProvider,
    AppCommandDbContext dbContext,
    ILogger<UserDefaultSeederManager> logger) : IScopedService, IUserDefaultSeederManager
{
    public async Task SeedAllForUserAsync(long userId, bool overrideData = false, CancellationToken ct = default)
    {
        var seeders = serviceProvider.GetServices<IUserDefaultSeeder>().ToList();

        var sortedSeeders = seeders.OrderBy(s => s.Order).ToList();

        foreach (var seeder in sortedSeeders)
        {
            var resetSuccessful = true;
            if (overrideData)
            {
                logger.LogInformation("Resetting {SeederName} for user {UserId}", seeder.SeederName, userId);
                resetSuccessful = await seeder.ResetDefaults(userId, ct);
            }
            if (!resetSuccessful && !overrideData)
            {
                continue;
            }
            logger.LogInformation("Seeding {SeederName} for user {UserId}", seeder.SeederName, userId);
            await seeder.SetupDefaults(userId, ct);
        }

        logger.LogInformation("Completed seeding all user defaults for user {UserId}", userId);
    }

    public async Task SeedAllForAllUsersAsync(bool overrideData = false, CancellationToken ct = default)
    {
        var userIds = await dbContext.Users.Select(u => u.Id).ToListAsync(ct);

        if (!userIds.Any())
        {
            logger.LogWarning("No users found in database. Skipping user default seeding.");
            return;
        }

        foreach (var userId in userIds)
        {
            await SeedAllForUserAsync(userId, overrideData, ct);
        }

        logger.LogInformation("Completed seeding all user defaults for {UserCount} users", userIds.Count);
    }
}
