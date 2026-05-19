using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.userDefault;

public class ActivityLocationTypeSeeder(
    AppDbContext dbContext,
    ILogger<ActivityLocationTypeSeeder> logger) : IScopedService, IUserDefaultSeeder
{
    public string SeederName => "ActivityLocationType";
    public int Order => 6;

    private static List<ActivityLocationType> Defaults(long userId) =>
    [
        new() { UserId = userId, Text = "Indoor",  SortOrder = 1 },
        new() { UserId = userId, Text = "Outdoor", SortOrder = 2 },
        new() { UserId = userId, Text = "Any",     SortOrder = 3 }
    ];

    public async Task SetupDefaults(long userId, CancellationToken ct = default)
    {
        var defaults = Defaults(userId);

        var existingCount = await dbContext.ActivityLocationTypes
            .Where(l => l.UserId == userId)
            .CountAsync(ct);

        if (defaults.Count <= existingCount)
        {
            logger.LogDebug("Activity location types for user {UserId} already exist, skipping.", userId);
            return;
        }

        await dbContext.ActivityLocationTypes.AddRangeAsync(defaults, ct);
        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation("Seeded activity location types for user {UserId}", userId);
    }

    public async Task<bool> ResetDefaults(long userId, CancellationToken ct = default)
    {
        var defaults = Defaults(userId);

        var existing = await dbContext.ActivityLocationTypes
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

        dbContext.ActivityLocationTypes.UpdateRange(existing);
        await dbContext.SaveChangesAsync(ct);

        return true;
    }
}
