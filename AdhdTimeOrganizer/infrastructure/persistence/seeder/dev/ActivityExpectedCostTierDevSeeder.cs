using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.dev;

public class ActivityExpectedCostTierDevSeeder(
    AppDbContext dbContext,
    ILogger<ActivityExpectedCostTierDevSeeder> logger) : IDevDatabaseSeeder, IScopedService
{
    public string SeederName => "ActivityExpectedCostTierDev";
    public int Order => 5;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableAsync<ActivityExpectedCostTier>();
    }

    public async Task SeedForUser(long userId)
    {
        (string Text, int SortOrder)[] custom =
        [
            ("Very Expensive",     5),
            ("Subscription-based", 6)
        ];

        var existing = await dbContext.ActivityExpectedCostTiers
            .Where(l => l.UserId == userId)
            .Select(l => l.Text)
            .ToListAsync();

        var toAdd = custom
            .Where(c => !existing.Contains(c.Text))
            .Select(c => new ActivityExpectedCostTier { UserId = userId, Text = c.Text, SortOrder = c.SortOrder })
            .ToList();

        if (toAdd.Count == 0) return;

        await dbContext.ActivityExpectedCostTiers.AddRangeAsync(toAdd);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} custom cost tiers for user {UserId}", toAdd.Count, userId);
    }
}
