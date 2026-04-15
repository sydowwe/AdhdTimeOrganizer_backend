using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.userDefault;

public class RoutineTimePeriodSeeder(
    AppDbContext dbContext,
    ILogger<RoutineTimePeriodSeeder> logger) : IScopedService, IUserDefaultSeeder
{
    public string SeederName => "RoutineTimePeriod";
    public int Order => 3;

    private static List<RoutineTimePeriod> Defaults(long userId) =>
    [
        new() { UserId = userId, Text = "Daily", Color = ColorPalette.Lime, LengthInDays = 1, StreakThreshold = 100, StreakGraceDays = 0, ResetAnchorDay = 0 },
        new() { UserId = userId, Text = "Weekly", Color = ColorPalette.Violet, LengthInDays = 7, StreakThreshold = 90, StreakGraceDays = 0, ResetAnchorDay = 1 },
        new() { UserId = userId, Text = "Monthly", Color = ColorPalette.Blue, LengthInDays = 30, StreakThreshold = 80, StreakGraceDays = 3, ResetAnchorDay = 1 },
        new() { UserId = userId, Text = "Yearly", Color = ColorPalette.Sky, LengthInDays = 365, StreakThreshold = 80, StreakGraceDays = 10, ResetAnchorDay = 1 }
    ];

    public async Task SetupDefaults(long userId, CancellationToken ct = default)
    {
        var defaults = Defaults(userId);

        var existingCount = await dbContext.RoutineTimePeriods
            .Where(rtp => rtp.UserId == userId)
            .CountAsync(ct);

        if (defaults.Count <= existingCount)
        {
            logger.LogDebug("Routine time periods for user {UserId} already exist, skipping.", userId);
            return;
        }

        await dbContext.RoutineTimePeriods.AddRangeAsync(defaults, ct);

        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation("Seeded routine time periods for user {UserId}", userId);
    }

    public async Task<bool> ResetDefaults(long userId, CancellationToken ct = default)
    {
        var defaults = Defaults(userId);

        var existing = await dbContext.RoutineTimePeriods
            .Where(rtp => rtp.UserId == userId)
            .OrderBy(rtp => rtp.Id)
            .Take(defaults.Count)
            .ToListAsync(ct);

        if (defaults.Count != existing.Count)
        {
            return false;
        }

        for (var i = 0; i < defaults.Count; i++)
        {
            existing[i].Text = defaults[i].Text;
            existing[i].Color = defaults[i].Color;
            existing[i].LengthInDays = defaults[i].LengthInDays;
            existing[i].ResetAnchorDay = defaults[i].ResetAnchorDay;
            existing[i].StreakThreshold = defaults[i].StreakThreshold;
            existing[i].StreakGraceDays = defaults[i].StreakGraceDays;
        }

        dbContext.RoutineTimePeriods.UpdateRange(existing);
        await dbContext.SaveChangesAsync(ct);

        return true;
    }
}