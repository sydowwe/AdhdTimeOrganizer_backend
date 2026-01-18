using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.userDefault;

public class DefaultActivityRoleSeeder(
    AppCommandDbContext dbContext,
    ILogger<DefaultActivityRoleSeeder> logger) : IScopedService, IUserDefaultSeeder
{
    public string SeederName => "DefaultActivityRole";
    public int Order => 4;

    private static List<ActivityRole> Defaults(long userId) =>
    [
        new() { UserId = userId, Name = "Planner task", Text = "Quickly created activities in task planner", Color = "#007bff", Icon = "fas fa-calendar-days" },
        new() { UserId = userId, Name = "To-do list task", Text = "Quickly created activities in to-do list", Color = "#009BCC", Icon = "fas fa-list-check" },
        new() { UserId = userId, Name = "Routine task", Text = "Quickly created activities in routine to-do list", Color = "#008080", Icon = "fas fa-recycle" }
    ];

    public async Task SetupDefaults(long userId, CancellationToken ct = default)
    {
        var defaults = Defaults(userId);

        var existingCount = await dbContext.ActivityRoles
            .Where(ar => ar.UserId == userId)
            .CountAsync(ct);

        if (defaults.Count <= existingCount)
        {
            logger.LogDebug("Default activity roles for user {UserId} already exist, skipping.", userId);
            return;
        }

        await dbContext.ActivityRoles.AddRangeAsync(defaults, ct);

        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation("Seeded default activity roles for user {UserId}", userId);
    }

    public async Task<bool> ResetDefaults(long userId, CancellationToken ct = default)
    {
        var defaults = Defaults(userId);

        var existing = await dbContext.ActivityRoles.Where(ar => ar.UserId == userId).OrderBy(ar => ar.Id).Take(defaults.Count).ToListAsync(ct);

        if (defaults.Count != existing.Count)
        {
            return false;
        }

        for (var i = 0; i < defaults.Count; i++)
        {
            existing[i].Name = defaults[i].Name;
            existing[i].Text = defaults[i].Text;
            existing[i].Color = defaults[i].Color;
            existing[i].Icon = defaults[i].Icon;
        }

        dbContext.ActivityRoles.UpdateRange(existing);
        await dbContext.SaveChangesAsync(ct);

        return true;
    }
}