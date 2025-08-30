using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.infrastructure.persistence.seeder;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.manager;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeders;

public class DefaultSeederManager(IServiceProvider serviceProvider) : IScopedService, IDefaultSeederManager
{
    public async Task SeedAllAsync(bool overrideData = true)
    {
        var seeders = serviceProvider.GetServices<IDefaultDatabaseSeeder>().ToList();

        if (overrideData)
        {
            var descSortedSeeders = seeders.OrderByDescending(s => s.Order).ToList();
            foreach (var seeder in descSortedSeeders)
            {
                await seeder.TruncateTable();
            }
        }

        var sortedSeeders = seeders.OrderBy(s => s.Order).ToList();

        foreach (var seeder in sortedSeeders)
        {
            await seeder.Seed();
        }
    }

    public async Task SeedAsync(string seederName)
    {
        var seeders = serviceProvider.GetServices<IDefaultDatabaseSeeder>();
        var seeder = seeders.FirstOrDefault(s => s.SeederName == seederName);

        if (seeder != null)
        {
            await seeder.Seed();
        }
        else
        {
            throw new InvalidOperationException($"Seeder with name '{seederName}' not found.");
        }
    }

    public IEnumerable<string> GetAllSeederNames()
    {
        var seeders = serviceProvider.GetServices<IDatabaseSeeder>();
        return seeders.OrderBy(s => s.Order).Select(s => s.SeederName);
    }
}