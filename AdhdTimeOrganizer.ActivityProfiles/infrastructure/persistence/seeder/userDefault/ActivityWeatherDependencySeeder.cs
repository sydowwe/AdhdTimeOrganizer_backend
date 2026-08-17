using AdhdTimeOrganizer.ActivityProfiles.domain.model;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.infrastructure.persistence.seeder;

namespace AdhdTimeOrganizer.ActivityProfiles.infrastructure.persistence.seeder.userDefault;

public class ActivityWeatherDependencySeeder(
    DbContext dbContext,
    ILogger<ActivityWeatherDependencySeeder> logger)
    : BasePerUserDefaultSeeder<ActivityWeatherDependency>(dbContext, logger), IScopedService
{
    public override string SeederName => "ActivityWeatherDependency";
    public override int Order => 21;
    protected override string EntityLabel => "activity weather dependencies";

    /// <summary>
    /// <c>Text</c> is the user's label and <c>Code</c> is what the weather signal actually matches on, so the
    /// two are seeded together and only the label is ever the user's to change. Seeding a row without a code
    /// would leave it depending on <c>WeatherDependencyCodes.Infer</c> guessing its own English label back —
    /// which works, and is exactly the fragility the column exists to remove.
    /// </summary>
    protected override List<ActivityWeatherDependency> Defaults(long userId) =>
    [
        new() { UserId = userId, Text = "None", SortOrder = 1, Code = WeatherDependencyCodes.None },
        new() { UserId = userId, Text = "Sunny", SortOrder = 2, Code = WeatherDependencyCodes.Sunny },
        new() { UserId = userId, Text = "Dry", SortOrder = 3, Code = WeatherDependencyCodes.Dry },
        new() { UserId = userId, Text = "Snow", SortOrder = 4, Code = WeatherDependencyCodes.Snow }
    ];

    /// <summary>Unique index: (user_id, text).</summary>
    protected override bool Collides(ActivityWeatherDependency a, ActivityWeatherDependency b) => a.Text == b.Text;

    protected override void Apply(ActivityWeatherDependency target, ActivityWeatherDependency @default)
    {
        target.Text = @default.Text;
        target.SortOrder = @default.SortOrder;
        target.Code = @default.Code;
    }
}
