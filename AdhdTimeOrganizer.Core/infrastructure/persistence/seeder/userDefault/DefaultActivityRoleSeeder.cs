using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.infrastructure.persistence.seeder;

namespace AdhdTimeOrganizer.Core.infrastructure.persistence.seeder.userDefault;

public class DefaultActivityRoleSeeder(
    DbContext dbContext,
    ILogger<DefaultActivityRoleSeeder> logger)
    : BasePerUserDefaultSeeder<ActivityRole>(dbContext, logger), IScopedService
{
    public override string SeederName => "DefaultActivityRole";
    public override int Order => 10;
    protected override string EntityLabel => "default activity roles";

    /// <summary>
    /// All three carry a <see cref="ActivityRole.SystemKey"/>: the app looks these up by key to file a
    /// quick-created activity, and the names below are only their initial display text — the user may
    /// rename or localize any of them without breaking that lookup.
    /// </summary>
    protected override List<ActivityRole> Defaults(long userId) =>
    [
        new() { UserId = userId, SystemKey = SystemActivityRole.PlannerTask, Name = "Planner task", Text = "Quickly created activities in task planner", Color = ColorPalette.Blue, Icon = "fas fa-calendar-days" },
        new() { UserId = userId, SystemKey = SystemActivityRole.TodoListTask, Name = "To-do list task", Text = "Quickly created activities in to-do list", Color = ColorPalette.Sky, Icon = "fas fa-list-check" },
        new() { UserId = userId, SystemKey = SystemActivityRole.RoutineTask, Name = "Routine task", Text = "Quickly created activities in routine to-do list", Color = ColorPalette.Teal, Icon = "fas fa-recycle" }
    ];

    /// <summary>
    /// Two unique indexes now: (user_id, name) and the filtered (user_id, system_key) — not Text,
    /// which is a description here. The key half is what keeps a renamed role recognisable, so a user
    /// who localized "Planner task" is not handed a second copy of it on the next setup pass.
    /// </summary>
    protected override bool Collides(ActivityRole a, ActivityRole b) =>
        a.Name == b.Name || (a.SystemKey is not null && a.SystemKey == b.SystemKey);

    protected override void Apply(ActivityRole target, ActivityRole @default)
    {
        target.Name = @default.Name;
        target.Text = @default.Text;
        target.Color = @default.Color;
        target.Icon = @default.Icon;
        target.SystemKey = @default.SystemKey;
    }
}
