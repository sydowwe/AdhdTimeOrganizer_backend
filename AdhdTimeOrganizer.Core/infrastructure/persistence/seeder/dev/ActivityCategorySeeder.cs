using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.infrastructure.persistence.seeder;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;

namespace AdhdTimeOrganizer.Core.infrastructure.persistence.seeder.dev;

public class ActivityCategorySeeder(
    DbContext dbContext,
    ILogger<ActivityCategorySeeder> logger) : IScopedService, IPerUserDevSeeder
{
    public string SeederName => "ActivityCategory";
    public int Order => 30;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableCascadeAsync<ActivityCategory>();
    }

    public async Task SeedForUser(long userId)
    {
        var activityCategories = new List<ActivityCategory>
        {
            new()
            {
                Name = "Urgent & Important",
                Text = "Critical tasks that need immediate attention",
                Color = ColorPalette.Red,
                UserId = userId
            },
            new()
            {
                Name = "Important Not Urgent",
                Text = "Important tasks that can be scheduled",
                Color = ColorPalette.Blue,
                UserId = userId
            },
            new()
            {
                Name = "Urgent Not Important",
                Text = "Tasks that need quick action but are less critical",
                Color = ColorPalette.Amber,
                UserId = userId
            },
            new()
            {
                Name = "Neither Urgent Nor Important",
                Text = "Low priority tasks to do when time permits",
                Color = ColorPalette.Zinc,
                UserId = userId
            },
            new()
            {
                Name = "Deep Work",
                Text = "Tasks requiring focused, uninterrupted concentration",
                Color = ColorPalette.Purple,
                UserId = userId
            },
            new()
            {
                Name = "Quick Tasks",
                Text = "Tasks that can be completed in under 15 minutes",
                Color = ColorPalette.Teal,
                UserId = userId
            },
            new()
            {
                Name = "Creative Work",
                Text = "Tasks involving creativity and brainstorming",
                Color = ColorPalette.Pink,
                UserId = userId
            },
            new()
            {
                Name = "Administrative",
                Text = "Routine administrative and organizational tasks",
                Color = ColorPalette.Slate,
                UserId = userId
            },
            new()
            {
                Name = "Maintenance",
                Text = "Regular upkeep and maintenance activities",
                Color = ColorPalette.Brown,
                UserId = userId
            },
            new()
            {
                Name = "Learning",
                Text = "Educational and skill development activities",
                Color = ColorPalette.Violet,
                UserId = userId
            }
        };

        await dbContext.Set<ActivityCategory>().AddRangeAsync(activityCategories);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} activity categories for user {UserId}", activityCategories.Count, userId);
    }
}