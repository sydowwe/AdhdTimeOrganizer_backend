using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.dev;

public class TaskPlannerDayTemplateSeeder(
    AppCommandDbContext dbContext,
    ILogger<TaskPlannerDayTemplateSeeder> logger) : IDevDatabaseSeeder, IScopedService
{
    public string SeederName => "TaskPlannerDayTemplate";
    public int Order => 11;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableAsync<TaskPlannerDayTemplate>();
    }

    public async Task SeedForUser(long userId)
    {
        List<TaskPlannerDayTemplate> templates =
        [
            new()
            {
                Name = "Home Office",
                Description = "Standard home office workday routine",
                Icon = "fas fa-home",
                IsActive = true,
                DefaultWakeUpTime = new TimeOnly(7, 0),
                DefaultBedTime = new TimeOnly(23, 0),
                SuggestedForDayType = DayType.Workday,
                Tags = ["productive", "focused", "work-from-home"],
                UsageCount = 0,
                UserId = userId
            },

            new()
            {
                Name = "Office Day",
                Description = "Standard office workday with commute",
                Icon = "fas fa-building",
                IsActive = true,
                DefaultWakeUpTime = new TimeOnly(6, 30),
                DefaultBedTime = new TimeOnly(22, 30),
                SuggestedForDayType = DayType.Workday,
                Tags = ["productive", "focused", "office", "commute"],
                UsageCount = 0,
                UserId = userId
            },

            new()
            {
                Name = "Relaxed Weekend",
                Description = "Balanced weekend with rest and activities",
                Icon = "fas fa-umbrella-beach",
                IsActive = true,
                DefaultWakeUpTime = new TimeOnly(9, 0),
                DefaultBedTime = new TimeOnly(23, 30),
                SuggestedForDayType = DayType.Weekend,
                Tags = ["relaxed", "balanced", "leisure"],
                UsageCount = 0,
                UserId = userId
            },

            new()
            {
                Name = "Productive Weekend",
                Description = "Weekend focused on personal projects and development",
                Icon = "fas fa-dumbbell",
                IsActive = true,
                DefaultWakeUpTime = new TimeOnly(8, 0),
                DefaultBedTime = new TimeOnly(23, 0),
                SuggestedForDayType = DayType.Weekend,
                Tags = ["productive", "self-improvement", "projects"],
                UsageCount = 0,
                UserId = userId
            },

            new()
            {
                Name = "Sick Day",
                Description = "Recovery day with minimal obligations",
                Icon = "fas fa-thermometer",
                IsActive = true,
                DefaultWakeUpTime = new TimeOnly(9, 0),
                DefaultBedTime = new TimeOnly(22, 0),
                SuggestedForDayType = DayType.SickDay,
                Tags = ["rest", "recovery", "minimal"],
                UsageCount = 0,
                UserId = userId
            },

            new()
            {
                Name = "Vacation",
                Description = "Holiday routine with leisure activities",
                Icon = "fas fa-plane",
                IsActive = true,
                DefaultWakeUpTime = new TimeOnly(9, 30),
                DefaultBedTime = new TimeOnly(0, 0),
                SuggestedForDayType = DayType.Vacation,
                Tags = ["relaxed", "leisure", "travel"],
                UsageCount = 0,
                UserId = userId
            }
        ];

        await dbContext.TaskPlannerDayTemplates.AddRangeAsync(templates);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} task planner day templates for user {UserId}", templates.Count, userId);
    }
}
