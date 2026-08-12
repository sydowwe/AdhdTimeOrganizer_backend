using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.infrastructure.persistence.seeder;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;

namespace AdhdTimeOrganizer.ActivityProfiles.infrastructure.persistence.seeder.dev;

public class ActivityLocationTypeDevSeeder(
    DbContext dbContext,
    ILogger<ActivityLocationTypeDevSeeder> logger) : IPerUserDevSeeder, IScopedService
{
    public string SeederName => "ActivityLocationTypeDev";
    public int Order => 10;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableCascadeAsync<ActivityLocationType>();
    }

    public async Task SeedForUser(long userId)
    {
        (string Text, int SortOrder)[] custom =
        [
            ("Remote", 4),
            ("Co-working Space", 5)
        ];

        var existing = await dbContext.Set<ActivityLocationType>()
            .Where(l => l.UserId == userId)
            .Select(l => l.Text)
            .ToListAsync();

        var toAdd = custom
            .Where(c => !existing.Contains(c.Text))
            .Select(c => new ActivityLocationType { UserId = userId, Text = c.Text, SortOrder = c.SortOrder })
            .ToList();

        if (toAdd.Count == 0)
            return;

        await dbContext.Set<ActivityLocationType>().AddRangeAsync(toAdd);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} custom location types for user {UserId}", toAdd.Count, userId);
    }
}