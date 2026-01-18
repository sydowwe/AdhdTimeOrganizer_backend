using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.dev;

public class ActivityCategorySeeder(
    AppCommandDbContext dbContext,
    ILogger<ActivityCategorySeeder> logger) : IScopedService, IDevDatabaseSeeder
{
    public string SeederName => "ActivityCategory";
    public int Order => 8;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableAsync<ActivityCategory>();
    }

    public async Task SeedForUser(long userId)
    {
        // Note: TruncateTable already removed existing data for this user

        var activityCategories = new List<ActivityCategory>
        {
            new()
            {
                Name = "Urgent & Important",
                Text = "Critical tasks that need immediate attention",
                Color = "#D32F2F", // Red
                UserId = userId
            },
            new()
            {
                Name = "Important Not Urgent",
                Text = "Important tasks that can be scheduled",
                Color = "#1976D2", // Blue
                UserId = userId
            },
            new()
            {
                Name = "Urgent Not Important",
                Text = "Tasks that need quick action but are less critical",
                Color = "#FFA000", // Amber
                UserId = userId
            },
            new()
            {
                Name = "Neither Urgent Nor Important",
                Text = "Low priority tasks to do when time permits",
                Color = "#757575", // Grey
                UserId = userId
            },
            new()
            {
                Name = "Deep Work",
                Text = "Tasks requiring focused, uninterrupted concentration",
                Color = "#7B1FA2", // Purple
                UserId = userId
            },
            new()
            {
                Name = "Quick Tasks",
                Text = "Tasks that can be completed in under 15 minutes",
                Color = "#00897B", // Teal
                UserId = userId
            },
            new()
            {
                Name = "Creative Work",
                Text = "Tasks involving creativity and brainstorming",
                Color = "#E91E63", // Pink
                UserId = userId
            },
            new()
            {
                Name = "Administrative",
                Text = "Routine administrative and organizational tasks",
                Color = "#546E7A", // Blue Grey
                UserId = userId
            },
            new()
            {
                Name = "Maintenance",
                Text = "Regular upkeep and maintenance activities",
                Color = "#795548", // Brown
                UserId = userId
            },
            new()
            {
                Name = "Learning",
                Text = "Educational and skill development activities",
                Color = "#5E35B1", // Deep Purple
                UserId = userId
            }
        };

        await dbContext.ActivityCategories.AddRangeAsync(activityCategories);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} activity categories for user {UserId}", activityCategories.Count, userId);
    }
}
