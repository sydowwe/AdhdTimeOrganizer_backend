using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.infrastructure.persistence.seeder;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;

namespace AdhdTimeOrganizer.ActivityProfiles.infrastructure.persistence.seeder.dev;

public class ActivityExpectedCostTierDevSeeder(
    DbContext dbContext,
    ILogger<ActivityExpectedCostTierDevSeeder> logger) : IPerUserDevSeeder, IScopedService
{
    public string SeederName => "ActivityExpectedCostTierDev";
    public int Order => 12;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableCascadeAsync<ActivityExpectedCostTier>();
    }

    public async Task SeedForUser(long userId)
    {
        (string Text, int SortOrder)[] custom =
        [
            ("Very Expensive", 5),
            ("Subscription-based", 6)
        ];

        var existing = await dbContext.Set<ActivityExpectedCostTier>()
            .Where(l => l.UserId == userId)
            .Select(l => l.Text)
            .ToListAsync();

        var toAdd = custom
            .Where(c => !existing.Contains(c.Text))
            .Select(c => new ActivityExpectedCostTier { UserId = userId, Text = c.Text, SortOrder = c.SortOrder })
            .ToList();

        if (toAdd.Count == 0)
            return;

        await dbContext.Set<ActivityExpectedCostTier>().AddRangeAsync(toAdd);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} custom cost tiers for user {UserId}", toAdd.Count, userId);
    }
}