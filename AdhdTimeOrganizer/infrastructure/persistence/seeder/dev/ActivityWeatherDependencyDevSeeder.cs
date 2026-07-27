using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.infrastructure.persistence.seeder;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.dev;

public class ActivityWeatherDependencyDevSeeder(
    AppDbContext dbContext,
    ILogger<ActivityWeatherDependencyDevSeeder> logger) : IPerUserDevSeeder, IScopedService
{
    public string SeederName => "ActivityWeatherDependencyDev";
    public int Order => 5;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableCascadeAsync<ActivityWeatherDependency>();
    }

    public async Task SeedForUser(long userId)
    {
        (string Text, int SortOrder)[] custom =
        [
            ("Rainy", 5),
            ("Cold", 6)
        ];

        var existing = await dbContext.ActivityWeatherDependencies
            .Where(l => l.UserId == userId)
            .Select(l => l.Text)
            .ToListAsync();

        var toAdd = custom
            .Where(c => !existing.Contains(c.Text))
            .Select(c => new ActivityWeatherDependency { UserId = userId, Text = c.Text, SortOrder = c.SortOrder })
            .ToList();

        if (toAdd.Count == 0)
            return;

        await dbContext.ActivityWeatherDependencies.AddRangeAsync(toAdd);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} custom weather dependencies for user {UserId}", toAdd.Count, userId);
    }
}