using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.infrastructure.persistence.seeder;

namespace AdhdTimeOrganizer.ActivityProfiles.infrastructure.persistence.seeder.userDefault;

public class ActivityExperienceTypeSeeder(
    DbContext dbContext,
    ILogger<ActivityExperienceTypeSeeder> logger)
    : BasePerUserDefaultSeeder<ActivityExperienceType>(dbContext, logger), IScopedService
{
    public override string SeederName => "ActivityExperienceType";
    public override int Order => 23;
    protected override string EntityLabel => "activity experience types";

    protected override List<ActivityExperienceType> Defaults(long userId) =>
    [
        new() { UserId = userId, Text = "Adrenaline", SortOrder = 1 },
        new() { UserId = userId, Text = "Travel", SortOrder = 2 },
        new() { UserId = userId, Text = "Skill", SortOrder = 3 },
        new() { UserId = userId, Text = "Culinary", SortOrder = 4 },
        new() { UserId = userId, Text = "Cultural", SortOrder = 5 }
    ];

    /// <summary>Unique index: (user_id, text).</summary>
    protected override bool Collides(ActivityExperienceType a, ActivityExperienceType b) => a.Text == b.Text;

    protected override void Apply(ActivityExperienceType target, ActivityExperienceType @default)
    {
        target.Text = @default.Text;
        target.SortOrder = @default.SortOrder;
    }
}
