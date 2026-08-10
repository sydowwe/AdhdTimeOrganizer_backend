using AdhdTimeOrganizer.Core.domain.model.entity.timer;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.infrastructure.persistence.seeder;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.infrastructure.persistence.seeder.userDefault;

public class PomodoroTimerPresetSeeder(
    DbContext dbContext,
    ILogger<PomodoroTimerPresetSeeder> logger)
    : BasePerUserDefaultSeeder<PomodoroTimerPreset>(dbContext, logger), IScopedService
{
    public override string SeederName => "PomodoroTimerPreset";
    public override int Order => 31;
    protected override string EntityLabel => "pomodoro timer presets";

    protected override List<PomodoroTimerPreset> Defaults(long userId) =>
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

    /// <summary>
    /// No unique index on this table, so nothing here can fail on a constraint — Name is the identity
    /// of a preset all the same, and matching on it keeps setup and reset idempotent.
    /// </summary>
    protected override bool Collides(PomodoroTimerPreset a, PomodoroTimerPreset b) => a.Name == b.Name;

    protected override void Apply(PomodoroTimerPreset target, PomodoroTimerPreset @default)
    {
        target.Name = @default.Name;
        target.FocusDuration = @default.FocusDuration;
        target.ShortBreakDuration = @default.ShortBreakDuration;
        target.LongBreakDuration = @default.LongBreakDuration;
        target.FocusPeriodInCycleCount = @default.FocusPeriodInCycleCount;
        target.NumberOfCycles = @default.NumberOfCycles;
        target.FocusActivityId = @default.FocusActivityId;
        target.RestActivityId = @default.RestActivityId;
    }
}
