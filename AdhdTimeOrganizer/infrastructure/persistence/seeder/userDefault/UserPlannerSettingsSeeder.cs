using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.userDefault;

public class UserPlannerSettingsSeeder(
    AppDbContext dbContext,
    ILogger<UserPlannerSettingsSeeder> logger) : IScopedService, IUserDefaultSeeder
{
    public string SeederName => "UserPlannerSettings";
    public int Order => 5;

    private static UserPlannerSettings Default(long userId) => new()
    {
        UserId = userId,
        RemindersEnabled = true,
        ReminderMinutesBefore = 10,
        DetailsPanelExpandedByDefault = true,
        ArrowKeyNavEnabled = true,
        PredefinedSkipReasons = []
    };

    public async Task SetupDefaults(long userId, CancellationToken ct = default)
    {
        var exists = await dbContext.UserPlannerSettings.AnyAsync(s => s.UserId == userId, ct);
        if (exists)
        {
            logger.LogDebug("Planner settings for user {UserId} already exist, skipping.", userId);
            return;
        }

        await dbContext.UserPlannerSettings.AddAsync(Default(userId), ct);
        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation("Seeded planner settings for user {UserId}", userId);
    }

    public async Task<bool> ResetDefaults(long userId, CancellationToken ct = default)
    {
        var existing = await dbContext.UserPlannerSettings.FirstOrDefaultAsync(s => s.UserId == userId, ct);
        if (existing == null)
            return false;

        var defaults = Default(userId);
        existing.RemindersEnabled = defaults.RemindersEnabled;
        existing.ReminderMinutesBefore = defaults.ReminderMinutesBefore;
        existing.DetailsPanelExpandedByDefault = defaults.DetailsPanelExpandedByDefault;
        existing.ArrowKeyNavEnabled = defaults.ArrowKeyNavEnabled;
        existing.PredefinedSkipReasons = defaults.PredefinedSkipReasons;

        dbContext.UserPlannerSettings.Update(existing);
        await dbContext.SaveChangesAsync(ct);

        return true;
    }
}
