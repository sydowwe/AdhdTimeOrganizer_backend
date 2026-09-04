using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.infrastructure.persistence.seeder;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;

namespace AdhdTimeOrganizer.ActivityProfiles.infrastructure.persistence.seeder.dev;

public class ActivityWeatherDependencyDevSeeder(
    DbContext dbContext,
    ILogger<ActivityWeatherDependencyDevSeeder> logger) : IPerUserDevSeeder, IScopedService
{
    public string SeederName => "ActivityWeatherDependencyDev";
    public int Order => 11;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableCascadeAsync<ActivityWeatherDependency>();
    }

    public async Task SeedForUser(long userId)
    {
        // Deliberately code-less, and deliberately unmatchable: "Rainy" and "Cold" map to none of the four
        // WeatherDependencyCodes and WeatherDependencyCodes.Infer guesses nothing from either label, so both
        // rows sit permanently outside the weather signal. That is the point — they stand in for the rows a
        // user invents themselves, so dev data exercises the "no opinion" path the signal degrades to. The
        // four codes belong to the userDefault seeder (order 21), which is the only place Code is ever set.
        (string Text, int SortOrder)[] custom =
        [
            ("Rainy", 5),
            ("Cold", 6)
        ];

        var existing = await dbContext.Set<ActivityWeatherDependency>()
            .Where(l => l.UserId == userId)
            .Select(l => l.Text)
            .ToListAsync();

        var toAdd = custom
            .Where(c => !existing.Contains(c.Text))
            .Select(c => new ActivityWeatherDependency { UserId = userId, Text = c.Text, SortOrder = c.SortOrder })
            .ToList();

        if (toAdd.Count == 0)
            return;

        await dbContext.Set<ActivityWeatherDependency>().AddRangeAsync(toAdd);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} custom weather dependencies for user {UserId}", toAdd.Count, userId);
    }
}