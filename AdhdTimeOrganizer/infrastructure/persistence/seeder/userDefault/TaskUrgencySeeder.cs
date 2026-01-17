using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence.seeders;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.userDefault;

public class TaskPrioritySeeder(
    AppCommandDbContext dbContext,
    ILogger<TaskPrioritySeeder> logger) : IScopedService, IUserDefaultSeeder
{
    public string SeederName => "TaskPriority";
    public int Order => 1;

    private static List<TaskPriority> Defaults(long userId) =>
    [
        new() { UserId = userId, Text = "Today", Color = "#FF5252", Priority = 1 }, // Red
        new() { UserId = userId, Text = "This week", Color = "#FFA726", Priority = 2 }, // Orange
        new() { UserId = userId, Text = "This month", Color = "#FFD600", Priority = 3 }, // Yellow
        new() { UserId = userId, Text = "This year", Color = "#4CAF50", Priority = 4 } // Green
    ];

    public async Task SetupDefaults(long userId, CancellationToken ct = default)
    {
        var defaults = Defaults(userId);

        var existingCount = await dbContext.TaskUrgencies
            .Where(tu => tu.UserId == userId)
            .CountAsync(ct);

        if (defaults.Count <= existingCount)
        {
            logger.LogDebug("Task urgencies for user {UserId} already exist, skipping.", userId);
            return;
        }

        await dbContext.TaskUrgencies.AddRangeAsync(defaults, ct);

        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation("Seeded task urgencies for user {UserId}", userId);
    }

    public async Task<bool> ResetDefaults(long userId, CancellationToken ct = default)
    {
        var defaults = Defaults(userId);

        var existing = await dbContext.TaskUrgencies
            .Where(tu => tu.UserId == userId)
            .OrderBy(tu => tu.Id)
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
            existing[i].Priority = defaults[i].Priority;
        }

        dbContext.TaskUrgencies.UpdateRange(existing);
        await dbContext.SaveChangesAsync(ct);

        return true;
    }
}
