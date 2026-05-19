using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.dev;

public class ActivityExperienceTypeDevSeeder(
    AppDbContext dbContext,
    ILogger<ActivityExperienceTypeDevSeeder> logger) : IDevDatabaseSeeder, IScopedService
{
    public string SeederName => "ActivityExperienceTypeDev";
    public int Order => 5;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableAsync<ActivityExperienceType>();
    }

    public async Task SeedForUser(long userId)
    {
        (string Text, int SortOrder)[] custom =
        [
            ("Creative",   6),
            ("Spiritual",  7)
        ];

        var existing = await dbContext.ActivityExperienceTypes
            .Where(l => l.UserId == userId)
            .Select(l => l.Text)
            .ToListAsync();

        var toAdd = custom
            .Where(c => !existing.Contains(c.Text))
            .Select(c => new ActivityExperienceType { UserId = userId, Text = c.Text, SortOrder = c.SortOrder })
            .ToList();

        if (toAdd.Count == 0) return;

        await dbContext.ActivityExperienceTypes.AddRangeAsync(toAdd);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} custom experience types for user {UserId}", toAdd.Count, userId);
    }
}
