using AdhdTimeOrganizer.domain.model.entity.timer;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.infrastructure.persistence.seeder;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.userDefault;

public class TimerPresetSeeder(
    AppDbContext dbContext,
    ILogger<TimerPresetSeeder> logger)
    : BasePerUserDefaultSeeder<TimerPreset>(dbContext, logger), IScopedService
{
    public override string SeederName => "TimerPreset";
    public override int Order => 10;
    protected override string EntityLabel => "timer presets";

    protected override List<TimerPreset> Defaults(long userId) =>
    [
        new() { UserId = userId, Duration = 15, ActivityId = null },
        new() { UserId = userId, Duration = 20, ActivityId = null },
        new() { UserId = userId, Duration = 30, ActivityId = null },
        new() { UserId = userId, Duration = 45, ActivityId = null },
        new() { UserId = userId, Duration = 60, ActivityId = null },
        new() { UserId = userId, Duration = 90, ActivityId = null },
        new() { UserId = userId, Duration = 120, ActivityId = null }
    ];

    /// <summary>
    /// No unique index on this table, so nothing here can fail on a constraint — Duration is the
    /// identity of a preset all the same, and matching on it keeps setup and reset idempotent.
    /// </summary>
    protected override bool Collides(TimerPreset a, TimerPreset b) => a.Duration == b.Duration;

    protected override void Apply(TimerPreset target, TimerPreset @default)
    {
        target.Duration = @default.Duration;
        target.ActivityId = @default.ActivityId;
    }
}
