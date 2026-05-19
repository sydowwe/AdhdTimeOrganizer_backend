using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.userDefault;

public class ActivityExpectedCostTierSeeder(
    AppDbContext dbContext,
    ILogger<ActivityExpectedCostTierSeeder> logger) : IScopedService, IUserDefaultSeeder
{
    public string SeederName => "ActivityExpectedCostTier";
    public int Order => 6;

    private static List<ActivityExpectedCostTier> Defaults(long userId) =>
    [
        new() { UserId = userId, Text = "Free",      SortOrder = 1 },
        new() { UserId = userId, Text = "Cheap",     SortOrder = 2 },
        new() { UserId = userId, Text = "Moderate",  SortOrder = 3 },
        new() { UserId = userId, Text = "Expensive", SortOrder = 4 }
    ];

    public async Task SetupDefaults(long userId, CancellationToken ct = default)
    {
        var defaults = Defaults(userId);

        var existingCount = await dbContext.ActivityExpectedCostTiers
            .Where(l => l.UserId == userId)
            .CountAsync(ct);

        if (defaults.Count <= existingCount)
        {
            logger.LogDebug("Activity expected cost tiers for user {UserId} already exist, skipping.", userId);
            return;
        }

        await dbContext.ActivityExpectedCostTiers.AddRangeAsync(defaults, ct);
        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation("Seeded activity expected cost tiers for user {UserId}", userId);
    }

    public async Task<bool> ResetDefaults(long userId, CancellationToken ct = default)
    {
        var defaults = Defaults(userId);

        var existing = await dbContext.ActivityExpectedCostTiers
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

        dbContext.ActivityExpectedCostTiers.UpdateRange(existing);
        await dbContext.SaveChangesAsync(ct);

        return true;
    }
}
