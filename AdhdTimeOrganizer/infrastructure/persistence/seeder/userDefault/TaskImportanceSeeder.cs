using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence.seeders;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.userDefault;

public class TaskImportanceSeeder(
    AppCommandDbContext dbContext,
    ILogger<TaskImportanceSeeder> logger) : IScopedService, IUserDefaultSeeder
{
    public string SeederName => "TaskImportance";
    public int Order => 2;

    private static List<TaskImportance> Defaults(long userId) =>
    [
        new() { UserId = userId, Text = "Critical", Color = "#FF5252", Importance = 999 }, // Red
        new() { UserId = userId, Text = "High", Color = "#FFA726", Importance = 888 }, // Orange
        new() { UserId = userId, Text = "Normal", Color = "#4287f5", Importance = 777 }, // Blue
        new() { UserId = userId, Text = "Low", Color = "#888", Importance = 666 } // Gray
    ];

    public async Task SetupDefaults(long userId, CancellationToken ct = default)
    {
        var defaults = Defaults(userId);

        var existingCount = await dbContext.TaskImportances
            .Where(ti => ti.UserId == userId)
            .CountAsync(ct);

        if (defaults.Count <= existingCount)
        {
            logger.LogDebug("Task importances for user {UserId} already exist, skipping.", userId);
            return;
        }

        await dbContext.TaskImportances.AddRangeAsync(defaults, ct);

        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation("Seeded task importances for user {UserId}", userId);
    }

    public async Task<bool> ResetDefaults(long userId, CancellationToken ct = default)
    {
        var defaults = Defaults(userId);

        var existing = await dbContext.TaskImportances
            .Where(ti => ti.UserId == userId)
            .OrderBy(ti => ti.Id)
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
            existing[i].Importance = defaults[i].Importance;
        }

        dbContext.TaskImportances.UpdateRange(existing);
        await dbContext.SaveChangesAsync(ct);

        return true;
    }
}
