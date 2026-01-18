using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface.manager;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder;

public class DevSeederManager(
    IServiceProvider serviceProvider,
    AppCommandDbContext dbContext,
    IUserDefaultSeederManager userDefaultSeederManager,
    ILogger<DevSeederManager> logger) : IScopedService, IDevSeederManager
{
    public async Task SeedAllAsync(bool overrideData = true)
    {
        var adminUserId = await GetRootAdminUserId();
        if (!adminUserId.HasValue)
        {
            logger.LogWarning("Root admin user not found. Skipping activity seeding.");
            return;
        }

        var devSeeders = serviceProvider.GetServices<IDevDatabaseSeeder>().ToList();

        if (overrideData)
        {
            var descSortedSeeders = devSeeders.OrderByDescending(s => s.Order).ToList();
            foreach (var seeder in descSortedSeeders)
            {
                await seeder.TruncateTable();
            }
        }

        dbContext.ChangeTracker.Clear();

        await userDefaultSeederManager.SeedAllForAllUsersAsync(false);

        // Sort by Priority (ascending order - lower numbers run first)
        var sortedDevSeeders = devSeeders.OrderBy(s => s.Order).ToList();

        // Execute each dev seeder in order
        foreach (var seeder in sortedDevSeeders)
        {
            await seeder.SeedForUser(adminUserId.Value);
        }
    }

    private async Task<long?> GetRootAdminUserId()
    {
        // Get root admin user ID for user default seeders
        var rootAdminUsername = Helper.GetEnvVar("ROOT_ADMIN_USERNAME");
        var adminUser = await dbContext.Users
            .FirstOrDefaultAsync(u => u.UserName == rootAdminUsername);

        if (adminUser == null)
        {
            logger.LogWarning("Root admin user not found. Cannot seed user defaults before dev data.");
        }

        return adminUser?.Id;
    }

    public IEnumerable<string> GetAllSeederNames()
    {
        var seeders = serviceProvider.GetServices<IDevDatabaseSeeder>();
        return seeders.OrderBy(s => s.Order).Select(s => s.SeederName);
    }
}