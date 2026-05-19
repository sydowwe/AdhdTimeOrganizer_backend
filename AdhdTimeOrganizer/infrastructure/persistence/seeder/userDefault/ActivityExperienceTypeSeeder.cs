using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.userDefault;

public class ActivityExperienceTypeSeeder(
    AppDbContext dbContext,
    ILogger<ActivityExperienceTypeSeeder> logger) : IScopedService, IUserDefaultSeeder
{
    public string SeederName => "ActivityExperienceType";
    public int Order => 6;

    private static List<ActivityExperienceType> Defaults(long userId) =>
    [
        new() { UserId = userId, Text = "Adrenaline", SortOrder = 1 },
        new() { UserId = userId, Text = "Travel",     SortOrder = 2 },
        new() { UserId = userId, Text = "Skill",      SortOrder = 3 },
        new() { UserId = userId, Text = "Culinary",   SortOrder = 4 },
        new() { UserId = userId, Text = "Cultural",   SortOrder = 5 }
    ];

    public async Task SetupDefaults(long userId, CancellationToken ct = default)
    {
        var defaults = Defaults(userId);

        var existingCount = await dbContext.ActivityExperienceTypes
            .Where(l => l.UserId == userId)
            .CountAsync(ct);

        if (defaults.Count <= existingCount)
        {
            logger.LogDebug("Activity experience types for user {UserId} already exist, skipping.", userId);
            return;
        }

        await dbContext.ActivityExperienceTypes.AddRangeAsync(defaults, ct);
        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation("Seeded activity experience types for user {UserId}", userId);
    }

    public async Task<bool> ResetDefaults(long userId, CancellationToken ct = default)
    {
        var defaults = Defaults(userId);

        var existing = await dbContext.ActivityExperienceTypes
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

        dbContext.ActivityExperienceTypes.UpdateRange(existing);
        await dbContext.SaveChangesAsync(ct);

        return true;
    }
}
