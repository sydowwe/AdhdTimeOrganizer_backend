using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.userDefault;

public class TaskPrioritySeeder(
    AppDbContext dbContext,
    ILogger<TaskPrioritySeeder> logger) : IScopedService, IUserDefaultSeeder
{
    public string SeederName => "TaskPriority";
    public int Order => 1;

    private static List<TaskPriority> Defaults(long userId) =>
    [
        new() { UserId = userId, Text = "Today", Color = ColorPalette.Red, Priority = 1 },
        new() { UserId = userId, Text = "This week", Color = ColorPalette.Yellow, Priority = 2 },
        new() { UserId = userId, Text = "This month", Color = ColorPalette.Blue, Priority = 3 },
        new() { UserId = userId, Text = "This year", Color = ColorPalette.Emerald, Priority = 4 }
    ];

    public async Task SetupDefaults(long userId, CancellationToken ct = default)
    {
        var defaults = Defaults(userId);

        var existingCount = await dbContext.TaskPriorities
            .Where(tu => tu.UserId == userId)
            .CountAsync(ct);

        if (defaults.Count <= existingCount)
        {
            logger.LogDebug("Task urgencies for user {UserId} already exist, skipping.", userId);
            return;
        }

        await dbContext.TaskPriorities.AddRangeAsync(defaults, ct);

        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation("Seeded task urgencies for user {UserId}", userId);
    }

    public async Task<bool> ResetDefaults(long userId, CancellationToken ct = default)
    {
        var defaults = Defaults(userId);

        var existing = await dbContext.TaskPriorities
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

        dbContext.TaskPriorities.UpdateRange(existing);
        await dbContext.SaveChangesAsync(ct);

        return true;
    }
}
