using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.dev;

public class ActivityRoleSeeder(
    AppDbContext dbContext,
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
            new()
            {
                Name = "Work",
                Text = "Professional and career-related activities",
                Color = ColorPalette.Blue,
                Icon = "fas fa-briefcase",
                UserId = userId
            },
            new()
            {
                Name = "Personal Development",
                Text = "Learning, growth, and self-improvement",
                Color = ColorPalette.Purple,
                Icon = "fas fa-book",
                UserId = userId
            },
            new()
            {
                Name = "Health & Fitness",
                Text = "Physical health, exercise, and wellness",
                Color = ColorPalette.Green,
                Icon = "fas fa-dumbbell",
                UserId = userId
            },
            new()
            {
                Name = "Social",
                Text = "Family, friends, and social interactions",
                Color = ColorPalette.Orange,
                Icon = "fas fa-users",
                UserId = userId
            },
            new()
            {
                Name = "Hobbies & Leisure",
                Text = "Recreation, entertainment, and enjoyment",
                Color = ColorPalette.Pink,
                Icon = "fas fa-gamepad",
                UserId = userId
            },
            new()
            {
                Name = "Household",
                Text = "Home maintenance, chores, and errands",
                Color = ColorPalette.Brown,
                Icon = "fas fa-house",
                UserId = userId
            },
            new()
            {
                Name = "Finance",
                Text = "Money management and financial planning",
                Color = ColorPalette.Emerald,
                Icon = "fas fa-money-bill-wave",
                UserId = userId
            },
            new()
            {
                Name = "Self-Care",
                Text = "Rest, relaxation, and mental well-being",
                Color = ColorPalette.Cyan,
                Icon = "fas fa-spa",
                UserId = userId
            }
        };

        await dbContext.ActivityRoles.AddRangeAsync(activityRoles);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} activity roles for user {UserId}", activityRoles.Count, userId);
    }
}
