using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.infrastructure.persistence.seeder;

namespace AdhdTimeOrganizer.Routines.infrastructure.persistence.seeder.userDefault;

public class RoutineTimePeriodSeeder(
    DbContext dbContext,
    ILogger<RoutineTimePeriodSeeder> logger)
    : BasePerUserDefaultSeeder<RoutineTimePeriod>(dbContext, logger), IScopedService
{
    public override string SeederName => "RoutineTimePeriod";
    public override int Order => 200;
    protected override string EntityLabel => "routine time periods";

    protected override List<RoutineTimePeriod> Defaults(long userId) =>
    [
        new() { UserId = userId, Text = "Daily", Color = ColorPalette.Lime, LengthInDays = 1, StreakThreshold = 100, StreakGraceDays = 0, ResetAnchorDay = 0 },
        new() { UserId = userId, Text = "Weekly", Color = ColorPalette.Violet, LengthInDays = 7, StreakThreshold = 90, StreakGraceDays = 0, ResetAnchorDay = 1 },
        new() { UserId = userId, Text = "Monthly", Color = ColorPalette.Blue, LengthInDays = 30, StreakThreshold = 80, StreakGraceDays = 3, ResetAnchorDay = 1 },
        new() { UserId = userId, Text = "Yearly", Color = ColorPalette.Sky, LengthInDays = 365, StreakThreshold = 80, StreakGraceDays = 10, ResetAnchorDay = 1 }
    ];

    /// <summary>
    /// Two unique indexes here: (user_id, text) and (user_id, length_in_days). Either one is enough
    /// to reject a row, so both have to gate seeding.
    /// </summary>
    protected override bool Collides(RoutineTimePeriod a, RoutineTimePeriod b) =>
        a.Text == b.Text || a.LengthInDays == b.LengthInDays;

    protected override void Apply(RoutineTimePeriod target, RoutineTimePeriod @default)
    {
        target.Text = @default.Text;
        target.Color = @default.Color;
        target.LengthInDays = @default.LengthInDays;
        target.ResetAnchorDay = @default.ResetAnchorDay;
        target.StreakThreshold = @default.StreakThreshold;
        target.StreakGraceDays = @default.StreakGraceDays;
    }
}
