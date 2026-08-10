using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.infrastructure.persistence.seeder;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.userDefault;

public class TaskImportanceSeeder(
    AppDbContext dbContext,
    ILogger<TaskImportanceSeeder> logger)
    : BasePerUserDefaultSeeder<TaskImportance>(dbContext, logger), IScopedService
{
    public override string SeederName => "TaskImportance";
    public override int Order => 2;
    protected override string EntityLabel => "task importances";

    protected override List<TaskImportance> Defaults(long userId) =>
    [
        new()
        {
            UserId = userId,
            Text = "Critical",
            Color = ColorPalette.Red,
            Icon = "fas fa-exclamation-triangle",
            Importance = TaskImportance.CriticalMarkerValue
        },
        new()
        {
            UserId = userId,
            Text = "Optional",
            Color = ColorPalette.Zinc,
            Icon = "fas fa-question-circle",
            Importance = TaskImportance.OptionalMarkerValue
        }
    ];

    /// <summary>Unique index: (user_id, importance) — not Text, which is free to repeat.</summary>
    protected override bool Collides(TaskImportance a, TaskImportance b) => a.Importance == b.Importance;

    protected override void Apply(TaskImportance target, TaskImportance @default)
    {
        target.Text = @default.Text;
        target.Color = @default.Color;
        target.Icon = @default.Icon;
        target.Importance = @default.Importance;
    }
}
