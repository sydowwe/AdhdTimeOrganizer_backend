using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.infrastructure.persistence.seeder;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.userDefault;

public class ActivityExpectedCostTierSeeder(
    AppDbContext dbContext,
    ILogger<ActivityExpectedCostTierSeeder> logger)
    : BasePerUserDefaultSeeder<ActivityExpectedCostTier>(dbContext, logger), IScopedService
{
    public override string SeederName => "ActivityExpectedCostTier";
    public override int Order => 6;
    protected override string EntityLabel => "activity expected cost tiers";

    protected override List<ActivityExpectedCostTier> Defaults(long userId) =>
    [
        new() { UserId = userId, Text = "Free", SortOrder = 1 },
        new() { UserId = userId, Text = "Cheap", SortOrder = 2 },
        new() { UserId = userId, Text = "Moderate", SortOrder = 3 },
        new() { UserId = userId, Text = "Expensive", SortOrder = 4 }
    ];

    /// <summary>Unique index: (user_id, text).</summary>
    protected override bool Collides(ActivityExpectedCostTier a, ActivityExpectedCostTier b) => a.Text == b.Text;

    protected override void Apply(ActivityExpectedCostTier target, ActivityExpectedCostTier @default)
    {
        target.Text = @default.Text;
        target.SortOrder = @default.SortOrder;
    }
}
