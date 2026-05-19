using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.userDefault;

public class ActivityWeatherDependencySeeder(
    AppDbContext dbContext,
    ILogger<ActivityWeatherDependencySeeder> logger) : IScopedService, IUserDefaultSeeder
{
    public string SeederName => "ActivityWeatherDependency";
    public int Order => 6;

    private static List<ActivityWeatherDependency> Defaults(long userId) =>
    [
        new() { UserId = userId, Text = "None",  SortOrder = 1 },
        new() { UserId = userId, Text = "Sunny", SortOrder = 2 },
        new() { UserId = userId, Text = "Dry",   SortOrder = 3 },
        new() { UserId = userId, Text = "Snow",  SortOrder = 4 }
    ];

    public async Task SetupDefaults(long userId, CancellationToken ct = default)
    {
        var defaults = Defaults(userId);

        var existingCount = await dbContext.ActivityWeatherDependencies
            .Where(l => l.UserId == userId)
            .CountAsync(ct);

        if (defaults.Count <= existingCount)
        {
            logger.LogDebug("Activity weather dependencies for user {UserId} already exist, skipping.", userId);
            return;
        }

        await dbContext.ActivityWeatherDependencies.AddRangeAsync(defaults, ct);
        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation("Seeded activity weather dependencies for user {UserId}", userId);
    }

    public async Task<bool> ResetDefaults(long userId, CancellationToken ct = default)
    {
        var defaults = Defaults(userId);

        var existing = await dbContext.ActivityWeatherDependencies
            .Where(l => l.UserId == userId)
            .OrderBy(l => l.Id)
            .Take(defaults.Count)
            .ToListAsync(ct);

        if (defaults.Count != existing.Count)
            return false;

        for (var i = 0; i < defaults.Count; i++)
        {
            existing[i].Text = defaults[i].Text;
            existing[i].SortOrder = defaults[i].SortOrder;
        }

        dbContext.ActivityWeatherDependencies.UpdateRange(existing);
        await dbContext.SaveChangesAsync(ct);

        return true;
    }
}
