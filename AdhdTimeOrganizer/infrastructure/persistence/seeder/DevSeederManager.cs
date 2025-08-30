using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.infrastructure.persistence.seeder;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.manager;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeders;

public class DevSeederManager(IServiceProvider serviceProvider) : IScopedService, IDevSeederManager
{
    public async Task SeedAllAsync(bool overrideData = true)
    {
        var devSeeders = serviceProvider.GetServices<IDevDatabaseSeeder>().ToList();

        if (overrideData)
        {
            var descSortedSeeders = devSeeders.OrderByDescending(s => s.Order).ToList();
            foreach (var seeder in descSortedSeeders)
            {
                await seeder.TruncateTable();
            }
        }

        // Sort by Priority (ascending order - lower numbers run first)
        var sortedDevSeeders = devSeeders.OrderBy(s => s.Order).ToList();

        // Execute each dev seeder in order
        foreach (var seeder in sortedDevSeeders)
        {
            await seeder.Seed();
        }
    }

    public async Task SeedAsync(string seederName)
    {
        var seeders = serviceProvider.GetServices<IDevDatabaseSeeder>();
        var seeder = seeders.FirstOrDefault(s => s.SeederName == seederName);

        if (seeder != null)
        {
            await seeder.Seed();
        }
        else
        {
            throw new InvalidOperationException($"Dev seeder with name '{seederName}' not found.");
        }
    }

    public IEnumerable<string> GetAllSeederNames()
    {
        var seeders = serviceProvider.GetServices<IDevDatabaseSeeder>();
        return seeders.OrderBy(s => s.Order).Select(s => s.SeederName);
    }
}