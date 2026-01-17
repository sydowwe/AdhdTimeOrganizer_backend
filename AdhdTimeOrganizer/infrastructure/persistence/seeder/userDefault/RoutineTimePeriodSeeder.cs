using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence.seeders;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.userDefault;

public class RoutineTimePeriodSeeder(
    AppCommandDbContext dbContext,
    ILogger<RoutineTimePeriodSeeder> logger) : IScopedService, IUserDefaultSeeder
{
    public string SeederName => "RoutineTimePeriod";
    public int Order => 3;

    private static List<RoutineTimePeriod> Defaults(long userId) =>
    [
        new() { UserId = userId, Text = "Daily", Color = "#92F58C", LengthInDays = 1 }, // Green
        new() { UserId = userId, Text = "Weekly", Color = "#936AF1", LengthInDays = 7 }, // Purple
        new() { UserId = userId, Text = "Monthly", Color = "#2C7EF4", LengthInDays = 30 }, // Blue
        new() { UserId = userId, Text = "Yearly", Color = "#A5CCF3", LengthInDays = 365 } // Light Blue
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
        }

        dbContext.RoutineTimePeriods.UpdateRange(existing);
        await dbContext.SaveChangesAsync(ct);

        return true;
    }
}
