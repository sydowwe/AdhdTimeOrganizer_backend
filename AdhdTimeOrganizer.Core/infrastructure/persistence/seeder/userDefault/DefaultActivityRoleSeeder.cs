using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.infrastructure.persistence.seeder;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.infrastructure.persistence.seeder.userDefault;

public class DefaultActivityRoleSeeder(
    DbContext dbContext,
    ILogger<DefaultActivityRoleSeeder> logger)
    : BasePerUserDefaultSeeder<ActivityRole>(dbContext, logger), IScopedService
{
    public override string SeederName => "DefaultActivityRole";
    public override int Order => 10;
    protected override string EntityLabel => "default activity roles";

    protected override List<ActivityRole> Defaults(long userId) =>
    [
        new() { UserId = userId, Name = "Planner task", Text = "Quickly created activities in task planner", Color = ColorPalette.Blue, Icon = "fas fa-calendar-days" },
        new() { UserId = userId, Name = "To-do list task", Text = "Quickly created activities in to-do list", Color = ColorPalette.Sky, Icon = "fas fa-list-check" },
        new() { UserId = userId, Name = "Routine task", Text = "Quickly created activities in routine to-do list", Color = ColorPalette.Teal, Icon = "fas fa-recycle" }
    ];

    /// <summary>Unique index: (user_id, name) — not Text, which is a description here.</summary>
    protected override bool Collides(ActivityRole a, ActivityRole b) => a.Name == b.Name;

    protected override void Apply(ActivityRole target, ActivityRole @default)
    {
        target.Name = @default.Name;
        target.Text = @default.Text;
        target.Color = @default.Color;
        target.Icon = @default.Icon;
    }
}
