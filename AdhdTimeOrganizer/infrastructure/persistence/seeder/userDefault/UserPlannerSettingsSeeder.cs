using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.infrastructure.persistence.seeder;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.userDefault;

public class UserPlannerSettingsSeeder(
    AppDbContext dbContext,
    ILogger<UserPlannerSettingsSeeder> logger)
    : BasePerUserDefaultSeeder<UserPlannerSettings>(dbContext, logger), IScopedService
{
    public override string SeederName => "UserPlannerSettings";
    public override int Order => 420;
    protected override string EntityLabel => "planner settings";

    protected override List<UserPlannerSettings> Defaults(long userId) =>
    [
        new()
        {
            UserId = userId,
            RemindersEnabled = true,
            ReminderMinutesBefore = 10,
            DetailsPanelExpandedByDefault = true,
            ArrowKeyNavEnabled = true,
            PredefinedSkipReasons = []
        }
    ];

    /// <summary>
    /// One row per user, so the user id alone is the key — and the query already scopes to it. Any
    /// existing row therefore *is* this default's row: nothing else to compare.
    /// </summary>
    protected override bool Collides(UserPlannerSettings a, UserPlannerSettings b) => true;

    protected override void Apply(UserPlannerSettings target, UserPlannerSettings @default)
    {
        target.RemindersEnabled = @default.RemindersEnabled;
        target.ReminderMinutesBefore = @default.ReminderMinutesBefore;
        target.DetailsPanelExpandedByDefault = @default.DetailsPanelExpandedByDefault;
        target.ArrowKeyNavEnabled = @default.ArrowKeyNavEnabled;
        target.PredefinedSkipReasons = @default.PredefinedSkipReasons;
    }
}
