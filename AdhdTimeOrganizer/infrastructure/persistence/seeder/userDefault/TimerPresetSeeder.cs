using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.timer;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.userDefault;

public class TimerPresetSeeder(
    AppCommandDbContext dbContext,
    ILogger<TimerPresetSeeder> logger) : IScopedService, IUserDefaultSeeder
{
    public string SeederName => "TimerPreset";
    public int Order => 10;

    private static List<TimerPreset> Defaults(long userId) =>
    [
        new() { UserId = userId, Duration = 15, ActivityId = null },
        new() { UserId = userId, Duration = 20, ActivityId = null },
        new() { UserId = userId, Duration = 30, ActivityId = null },
        new() { UserId = userId, Duration = 45, ActivityId = null },
        new() { UserId = userId, Duration = 60, ActivityId = null },
        new() { UserId = userId, Duration = 90, ActivityId = null },
        new() { UserId = userId, Duration = 120, ActivityId = null }
    ];

    public async Task SetupDefaults(long userId, CancellationToken ct = default)
    {
        var defaults = Defaults(userId);

        var existingCount = await dbContext.TimerPresets
            .Where(tp => tp.UserId == userId)
            .CountAsync(ct);

        if (defaults.Count <= existingCount)
        {
            logger.LogDebug("Timer presets for user {UserId} already exist, skipping.", userId);
            return;
        }

        await dbContext.TimerPresets.AddRangeAsync(defaults, ct);

        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation("Seeded timer presets for user {UserId}", userId);
    }

    public async Task<bool> ResetDefaults(long userId, CancellationToken ct = default)
    {
        var defaults = Defaults(userId);

        var existing = await dbContext.TimerPresets
            .Where(tp => tp.UserId == userId)
            .OrderBy(tp => tp.Id)
            .Take(defaults.Count)
            .ToListAsync(ct);

        if (defaults.Count != existing.Count)
        {
            return false;
        }

        for (var i = 0; i < defaults.Count; i++)
        {
            existing[i].Duration = defaults[i].Duration;
            existing[i].ActivityId = defaults[i].ActivityId;
        }

        dbContext.TimerPresets.UpdateRange(existing);
        await dbContext.SaveChangesAsync(ct);

        return true;
    }
}
