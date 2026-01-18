using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface.manager;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder;

public class DefaultSeederManager(IServiceProvider serviceProvider) : IScopedService, IDefaultSeederManager
{
    public async Task SeedAllAsync(bool overrideData = true)
    {
        var seeders = serviceProvider.GetServices<IDefaultDatabaseSeeder>().ToList();

        var sortedSeeders = seeders.OrderBy(s => s.Order).ToList();

        foreach (var seeder in sortedSeeders)
        {
            await seeder.Seed(overrideData);
        }
    }

    public async Task SeedAsync(string seederName)
    {
        var seeders = serviceProvider.GetServices<IDefaultDatabaseSeeder>();
        var seeder = seeders.FirstOrDefault(s => s.SeederName == seederName);

        if (seeder != null)
        {
            await seeder.Seed(false);
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