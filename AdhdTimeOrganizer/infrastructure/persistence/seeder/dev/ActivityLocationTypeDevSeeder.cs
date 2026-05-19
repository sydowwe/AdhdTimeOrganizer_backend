using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.dev;

public class ActivityLocationTypeDevSeeder(
    AppDbContext dbContext,
    ILogger<ActivityLocationTypeDevSeeder> logger) : IDevDatabaseSeeder, IScopedService
{
    public string SeederName => "ActivityLocationTypeDev";
    public int Order => 5;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableAsync<ActivityLocationType>();
    }

    public async Task SeedForUser(long userId)
    {
        (string Text, int SortOrder)[] custom =
        [
            ("Remote",            4),
            ("Co-working Space",  5)
        ];

        var existing = await dbContext.ActivityLocationTypes
            .Where(l => l.UserId == userId)
            .Select(l => l.Text)
            .ToListAsync();

        var toAdd = custom
            .Where(c => !existing.Contains(c.Text))
            .Select(c => new ActivityLocationType { UserId = userId, Text = c.Text, SortOrder = c.SortOrder })
            .ToList();

        if (toAdd.Count == 0) return;

        await dbContext.ActivityLocationTypes.AddRangeAsync(toAdd);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} custom location types for user {UserId}", toAdd.Count, userId);
    }
}
