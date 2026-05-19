using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.activity.profile;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.dev;

public class ActivityProjectProfileSeeder(
    AppDbContext dbContext,
    ILogger<ActivityProjectProfileSeeder> logger) : IDevDatabaseSeeder, IScopedService
{
    public string SeederName => "ActivityDiyProfile";
    public int Order => 11;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableAsync<ActivityProjectProfile>();
    }

    public async Task SeedForUser(long userId)
    {
        if (await dbContext.Set<ActivityProjectProfile>().AnyAsync())
            return;

        var activities = await dbContext.Activities
            .Where(a => a.UserId == userId)
            .ToListAsync();

        if (activities.Count == 0)
        {
            logger.LogWarning("No activities found for user {UserId}, skipping ActivityDiyProfile seeding", userId);
            return;
        }

        long? GetId(string name) => activities.FirstOrDefault(a => a.Name == name)?.Id;

        var profiles = new List<ActivityProjectProfile>();

        void Add(string name, DifficultyLevel difficulty, string projectArea, decimal estimatedHours,
            bool isMessy, List<string> materials, List<string> tools, ReadinessStatus readiness)
        {
            var activityId = GetId(name);
            if (activityId == null) return;
            profiles.Add(new ActivityProjectProfile
            {
                ActivityId = activityId.Value,
                DifficultyLevel = difficulty,
                ProjectArea = projectArea,
                EstimatedHours = estimatedHours,
                IsMessy = isMessy,
                MaterialsNeeded = materials,
                RequiredTools = tools,
                ReadinessStatus = readiness
            });
        }

        // Covers DifficultyLevel: Beginner, Intermediate, Expert
        // Covers ReadinessStatus: Planning, NeedsShopping, ReadyToStart
        Add("Laundry",
            DifficultyLevel.Beginner, "Household Chores", 1.5m, false,
            ["laundry detergent", "fabric softener", "dryer sheets", "stain remover"],
            ["washing machine", "dryer", "laundry basket"],
            ReadinessStatus.NeedsShopping);

        Add("Meal Preparation",
            DifficultyLevel.Intermediate, "Culinary", 2.0m, true,
            ["chicken breast", "vegetables", "olive oil", "spices", "rice", "fresh herbs"],
            ["chef's knife", "cutting board", "frying pan", "pot", "measuring cups"],
            ReadinessStatus.ReadyToStart);

        Add("Practice Coding",
            DifficultyLevel.Intermediate, "Technology", 3.0m, false,
            ["laptop", "internet connection", "coding challenge platform subscription"],
            ["IDE", "terminal", "debugger", "version control"],
            ReadinessStatus.ReadyToStart);

        Add("Bug Fixing",
            DifficultyLevel.Expert, "Technology", 4.0m, false,
            ["error logs", "test cases", "documentation", "reproduction steps"],
            ["debugger", "profiler", "version control", "monitoring tools"],
            ReadinessStatus.Planning);

        Add("House Cleaning",
            DifficultyLevel.Beginner, "Household Chores", 2.5m, true,
            ["all-purpose cleaner", "disinfectant spray", "mop pads", "sponges", "rubber gloves", "trash bags"],
            ["vacuum cleaner", "mop", "broom", "dustpan"],
            ReadinessStatus.ReadyToStart);

        Add("Side Project",
            DifficultyLevel.Expert, "Software Development", 20.0m, false,
            ["cloud hosting plan", "domain name", "API keys", "design assets", "market research notes"],
            ["IDE", "Docker", "CI/CD pipeline", "database client", "Figma"],
            ReadinessStatus.Planning);

        await dbContext.Set<ActivityProjectProfile>().AddRangeAsync(profiles);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} DIY profiles for user {UserId}", profiles.Count, userId);
    }
}
