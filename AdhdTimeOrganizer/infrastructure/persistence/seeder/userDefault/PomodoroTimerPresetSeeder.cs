using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.timer;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.userDefault;

public class PomodoroTimerPresetSeeder(
    AppCommandDbContext dbContext,
    ILogger<PomodoroTimerPresetSeeder> logger) : IScopedService, IUserDefaultSeeder
{
    public string SeederName => "PomodoroTimerPreset";
    public int Order => 11;

    private static List<PomodoroTimerPreset> Defaults(long userId) =>
    [
        new()
        {
            UserId = userId,
            Name = "Classic Pomodoro",
            FocusDuration = 25,
            ShortBreakDuration = 5,
            LongBreakDuration = 15,
            FocusPeriodInCycleCount = 4,
            NumberOfCycles = 2,
            FocusActivityId = null,
            RestActivityId = null
        },
        new()
        {
            UserId = userId,
            Name = "Extended Focus",
            FocusDuration = 50,
            ShortBreakDuration = 10,
            LongBreakDuration = 30,
            FocusPeriodInCycleCount = 3,
            NumberOfCycles = 2,
            FocusActivityId = null,
            RestActivityId = null
        },
        new()
        {
            UserId = userId,
            Name = "Short Sprint",
            FocusDuration = 15,
            ShortBreakDuration = 3,
            LongBreakDuration = 10,
            FocusPeriodInCycleCount = 4,
            NumberOfCycles = 3,
            FocusActivityId = null,
            RestActivityId = null
        },
        new()
        {
            UserId = userId,
            Name = "Deep Work",
            FocusDuration = 90,
            ShortBreakDuration = 15,
            LongBreakDuration = 30,
            FocusPeriodInCycleCount = 2,
            NumberOfCycles = 2,
            FocusActivityId = null,
            RestActivityId = null
        }
    ];

    public async Task SetupDefaults(long userId, CancellationToken ct = default)
    {
        var defaults = Defaults(userId);

        var existingCount = await dbContext.PomodoroTimerPresets
            .Where(ptp => ptp.UserId == userId)
            .CountAsync(ct);

        if (defaults.Count <= existingCount)
        {
            logger.LogDebug("Pomodoro timer presets for user {UserId} already exist, skipping.", userId);
            return;
        }

        await dbContext.PomodoroTimerPresets.AddRangeAsync(defaults, ct);

        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation("Seeded pomodoro timer presets for user {UserId}", userId);
    }

    public async Task<bool> ResetDefaults(long userId, CancellationToken ct = default)
    {
        var defaults = Defaults(userId);

        var existing = await dbContext.PomodoroTimerPresets
            .Where(ptp => ptp.UserId == userId)
            .OrderBy(ptp => ptp.Id)
            .Take(defaults.Count)
            .ToListAsync(ct);

        if (defaults.Count != existing.Count)
        {
            return false;
        }

        for (var i = 0; i < defaults.Count; i++)
        {
            existing[i].Name = defaults[i].Name;
            existing[i].FocusDuration = defaults[i].FocusDuration;
            existing[i].ShortBreakDuration = defaults[i].ShortBreakDuration;
            existing[i].LongBreakDuration = defaults[i].LongBreakDuration;
            existing[i].FocusPeriodInCycleCount = defaults[i].FocusPeriodInCycleCount;
            existing[i].NumberOfCycles = defaults[i].NumberOfCycles;
            existing[i].FocusActivityId = defaults[i].FocusActivityId;
            existing[i].RestActivityId = defaults[i].RestActivityId;
        }

        dbContext.PomodoroTimerPresets.UpdateRange(existing);
        await dbContext.SaveChangesAsync(ct);

        return true;
    }
}
