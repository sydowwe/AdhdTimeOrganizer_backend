using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.infrastructure.persistence.seeder;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.userDefault;

public class ActivityLocationTypeSeeder(
    AppDbContext dbContext,
    ILogger<ActivityLocationTypeSeeder> logger)
    : BasePerUserDefaultSeeder<ActivityLocationType>(dbContext, logger), IScopedService
{
    public override string SeederName => "ActivityLocationType";
    public override int Order => 6;
    protected override string EntityLabel => "activity location types";

    protected override List<ActivityLocationType> Defaults(long userId) =>
    [
        new() { UserId = userId, Text = "Indoor", SortOrder = 1 },
        new() { UserId = userId, Text = "Outdoor", SortOrder = 2 },
        new() { UserId = userId, Text = "Any", SortOrder = 3 }
    ];

    /// <summary>Unique index: (user_id, text).</summary>
    protected override bool Collides(ActivityLocationType a, ActivityLocationType b) => a.Text == b.Text;

    protected override void Apply(ActivityLocationType target, ActivityLocationType @default)
    {
        target.Text = @default.Text;
        target.SortOrder = @default.SortOrder;
    }
}
