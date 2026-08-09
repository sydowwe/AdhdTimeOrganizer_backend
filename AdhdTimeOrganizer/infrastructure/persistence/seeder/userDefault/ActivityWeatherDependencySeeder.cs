using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.infrastructure.persistence.seeder;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.userDefault;

public class ActivityWeatherDependencySeeder(
    AppDbContext dbContext,
    ILogger<ActivityWeatherDependencySeeder> logger)
    : BasePerUserDefaultSeeder<ActivityWeatherDependency>(dbContext, logger), IScopedService
{
    public override string SeederName => "ActivityWeatherDependency";
    public override int Order => 6;
    protected override string EntityLabel => "activity weather dependencies";

    protected override List<ActivityWeatherDependency> Defaults(long userId) =>
    [
        new() { UserId = userId, Text = "None", SortOrder = 1 },
        new() { UserId = userId, Text = "Sunny", SortOrder = 2 },
        new() { UserId = userId, Text = "Dry", SortOrder = 3 },
        new() { UserId = userId, Text = "Snow", SortOrder = 4 }
    ];

    /// <summary>Unique index: (user_id, text).</summary>
    protected override bool Collides(ActivityWeatherDependency a, ActivityWeatherDependency b) => a.Text == b.Text;

    protected override void Apply(ActivityWeatherDependency target, ActivityWeatherDependency @default)
    {
        target.Text = @default.Text;
        target.SortOrder = @default.SortOrder;
    }
}
