using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.infrastructure.persistence.seeder;

namespace AdhdTimeOrganizer.TodoLists.infrastructure.persistence.seeder.userDefault;

public class TaskPrioritySeeder(
    DbContext dbContext,
    ILogger<TaskPrioritySeeder> logger)
    : BasePerUserDefaultSeeder<TaskPriority>(dbContext, logger), IScopedService
{
    public override string SeederName => "TaskPriority";
    public override int Order => 100;
    protected override string EntityLabel => "task priorities";

    protected override List<TaskPriority> Defaults(long userId) =>
    [
        new() { UserId = userId, Text = "Today", Color = ColorPalette.Red, Priority = 1 },
        new() { UserId = userId, Text = "This week", Color = ColorPalette.Yellow, Priority = 2 },
        new() { UserId = userId, Text = "This month", Color = ColorPalette.Blue, Priority = 3 },
        new() { UserId = userId, Text = "This year", Color = ColorPalette.Emerald, Priority = 4 }
    ];

    /// <summary>Unique index: (user_id, priority) — not Text, which is free to repeat.</summary>
    protected override bool Collides(TaskPriority a, TaskPriority b) => a.Priority == b.Priority;

    protected override void Apply(TaskPriority target, TaskPriority @default)
    {
        target.Text = @default.Text;
        target.Color = @default.Color;
        target.Priority = @default.Priority;
    }
}
