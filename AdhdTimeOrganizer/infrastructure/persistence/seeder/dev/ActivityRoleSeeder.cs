using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence.seeders;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.@default;

public class ActivityRoleSeeder(
    AppCommandDbContext dbContext,
    ILogger<ActivityRoleSeeder> logger) : IScopedService, IDevDatabaseSeeder
{
    public string SeederName => "ActivityRole";
    public int Order => 7;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableAsync<ActivityRole>();
    }

    public async Task SeedForUser(long userId)
    {
        var activityRoles = new List<ActivityRole>
        {
            new ActivityRole
            {
                Name = "Work",
                Text = "Professional and career-related activities",
                Color = "#1976D2", // Blue
                Icon = "💼",
                UserId = userId
            },
            new ActivityRole
            {
                Name = "Personal Development",
                Text = "Learning, growth, and self-improvement",
                Color = "#7B1FA2", // Purple
                Icon = "📚",
                UserId = userId
            },
            new ActivityRole
            {
                Name = "Health & Fitness",
                Text = "Physical health, exercise, and wellness",
                Color = "#388E3C", // Green
                Icon = "💪",
                UserId = userId
            },
            new ActivityRole
            {
                Name = "Social",
                Text = "Family, friends, and social interactions",
                Color = "#F57C00", // Orange
                Icon = "👥",
                UserId = userId
            },
            new ActivityRole
            {
                Name = "Hobbies & Leisure",
                Text = "Recreation, entertainment, and enjoyment",
                Color = "#E91E63", // Pink
                Icon = "🎮",
                UserId = userId
            },
            new ActivityRole
            {
                Name = "Household",
                Text = "Home maintenance, chores, and errands",
                Color = "#795548", // Brown
                Icon = "🏠",
                UserId = userId
            },
            new ActivityRole
            {
                Name = "Finance",
                Text = "Money management and financial planning",
                Color = "#4CAF50", // Light Green
                Icon = "💰",
                UserId = userId
            },
            new ActivityRole
            {
                Name = "Self-Care",
                Text = "Rest, relaxation, and mental well-being",
                Color = "#00BCD4", // Cyan
                Icon = "🧘",
                UserId = userId
            }
        };

        await dbContext.ActivityRoles.AddRangeAsync(activityRoles);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} activity roles for user {UserId}", activityRoles.Count, userId);
    }
}
