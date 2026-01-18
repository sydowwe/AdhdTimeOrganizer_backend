using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.dev;

public class TemplatePlannerTaskSeeder(
    AppCommandDbContext dbContext,
    ILogger<TemplatePlannerTaskSeeder> logger) : IDevDatabaseSeeder, IScopedService
{
    public string SeederName => "TemplatePlannerTask";
    public int Order => 12;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableAsync<TemplatePlannerTask>();
    }

    public async Task SeedForUser(long userId)
    {
        // Get templates and activities for this user
        var templates = await dbContext.TaskPlannerDayTemplates
            .Where(t => t.UserId == userId)
            .ToListAsync();

        var activities = await dbContext.Activities
            .Where(a => a.UserId == userId)
            .ToListAsync();

        var importances = await dbContext.TaskImportances
            .Where(i => i.UserId == userId)
            .ToListAsync();

        if (!templates.Any())
        {
            logger.LogWarning("No task planner day templates found for user {UserId}. Skipping template planner task seeding.", userId);
            return;
        }

        if (!activities.Any())
        {
            logger.LogWarning("No activities found for user {UserId}. Skipping template planner task seeding.", userId);
            return;
        }

        // Get specific templates
        var homeOfficeTemplate = templates.FirstOrDefault(t => t.Name == "Home Office");
        var officeTemplate = templates.FirstOrDefault(t => t.Name == "Office Day");
        var relaxedWeekendTemplate = templates.FirstOrDefault(t => t.Name == "Relaxed Weekend");

        // Get specific activities
        var morningExercise = activities.FirstOrDefault(a => a.Name == "Morning Exercise");
        var meditation = activities.FirstOrDefault(a => a.Name == "Meditation");
        var featureDev = activities.FirstOrDefault(a => a.Name == "Feature Development");
        var dailyStandup = activities.FirstOrDefault(a => a.Name == "Daily Standup");
        var mealPrep = activities.FirstOrDefault(a => a.Name == "Meal Preparation");
        var sleepRoutine = activities.FirstOrDefault(a => a.Name == "Sleep Routine");
        var gaming = activities.FirstOrDefault(a => a.Name == "Gaming Session");
        var sideProject = activities.FirstOrDefault(a => a.Name == "Side Project");
        var codeReview = activities.FirstOrDefault(a => a.Name == "Code Review");
        var bugFixing = activities.FirstOrDefault(a => a.Name == "Bug Fixing");
        var laundry = activities.FirstOrDefault(a => a.Name == "Laundry");
        var houseCleaning = activities.FirstOrDefault(a => a.Name == "House Cleaning");
        var groceryShopping = activities.FirstOrDefault(a => a.Name == "Grocery Shopping");
        var watchMovie = activities.FirstOrDefault(a => a.Name == "Watch Movie/Series");

        // Get importance levels
        var criticalImportance = importances.FirstOrDefault(i => i.Importance == 999) ?? throw new InvalidOperationException("Critical importance level missing.");
        var highImportance = importances.FirstOrDefault(i => i.Importance == 888) ?? throw new InvalidOperationException("High importance level missing.");
        var mediumImportance = importances.FirstOrDefault(i => i.Importance == 777) ?? throw new InvalidOperationException("Medium importance level missing.");
        var optionalImportance = importances.FirstOrDefault(i => i.Importance == 666) ?? throw new InvalidOperationException("Optional importance level missing.");

        var templateTasks = new List<TemplatePlannerTask>();

        // Home Office template tasks
        if (homeOfficeTemplate != null)
        {
            if (morningExercise != null)
            {
                templateTasks.Add(new TemplatePlannerTask
                {
                    TemplateId = homeOfficeTemplate.Id,
                    ActivityId = morningExercise.Id,
                    StartTime = new TimeOnly(7, 30),
                    EndTime = new TimeOnly(8, 30),
                    IsBackground = false,
                    ImportanceId = mediumImportance.Id,
                    Location = "Home",
                    Notes = "Morning workout to start the day",
                    UserId = userId
                });
            }

            if (dailyStandup != null)
            {
                templateTasks.Add(new TemplatePlannerTask
                {
                    TemplateId = homeOfficeTemplate.Id,
                    ActivityId = dailyStandup.Id,
                    StartTime = new TimeOnly(9, 0),
                    EndTime = new TimeOnly(9, 15),
                    IsBackground = false,
                    ImportanceId = criticalImportance.Id,
                    Location = "Home",
                    Notes = "Daily team sync",
                    UserId = userId
                });
            }

            if (featureDev != null)
            {
                templateTasks.Add(new TemplatePlannerTask
                {
                    TemplateId = homeOfficeTemplate.Id,
                    ActivityId = featureDev.Id,
                    StartTime = new TimeOnly(9, 30),
                    EndTime = new TimeOnly(12, 30),
                    IsBackground = false,
                    ImportanceId = highImportance.Id,
                    Location = "Home",
                    Notes = "Deep work session",
                    UserId = userId
                });
            }

            if (mealPrep != null)
            {
                templateTasks.Add(new TemplatePlannerTask
                {
                    TemplateId = homeOfficeTemplate.Id,
                    ActivityId = mealPrep.Id,
                    StartTime = new TimeOnly(12, 30),
                    EndTime = new TimeOnly(13, 0),
                    IsBackground = false,
                    ImportanceId = mediumImportance.Id,
                    Location = "Home",
                    UserId = userId
                });
            }

            if (bugFixing != null)
            {
                templateTasks.Add(new TemplatePlannerTask
                {
                    TemplateId = homeOfficeTemplate.Id,
                    ActivityId = bugFixing.Id,
                    StartTime = new TimeOnly(14, 0),
                    EndTime = new TimeOnly(16, 0),
                    IsBackground = false,
                    ImportanceId = highImportance.Id,
                    Location = "Home",
                    Notes = "Bug fixing session",
                    UserId = userId
                });
            }

            if (codeReview != null)
            {
                templateTasks.Add(new TemplatePlannerTask
                {
                    TemplateId = homeOfficeTemplate.Id,
                    ActivityId = codeReview.Id,
                    StartTime = new TimeOnly(16, 0),
                    EndTime = new TimeOnly(17, 0),
                    IsBackground = false,
                    ImportanceId = mediumImportance.Id,
                    Location = "Home",
                    Notes = "Review PRs",
                    UserId = userId
                });
            }

            if (sleepRoutine != null)
            {
                templateTasks.Add(new TemplatePlannerTask
                {
                    TemplateId = homeOfficeTemplate.Id,
                    ActivityId = sleepRoutine.Id,
                    StartTime = new TimeOnly(22, 30),
                    EndTime = new TimeOnly(23, 0),
                    IsBackground = false,
                    ImportanceId = highImportance.Id,
                    Location = "Home",
                    UserId = userId
                });
            }
        }

        // Office Day template tasks
        if (officeTemplate != null)
        {
            if (dailyStandup != null)
            {
                templateTasks.Add(new TemplatePlannerTask
                {
                    TemplateId = officeTemplate.Id,
                    ActivityId = dailyStandup.Id,
                    StartTime = new TimeOnly(9, 30),
                    EndTime = new TimeOnly(9, 45),
                    IsBackground = false,
                    ImportanceId = criticalImportance.Id,
                    Location = "Office",
                    Notes = "Daily team sync",
                    UserId = userId
                });
            }

            if (featureDev != null)
            {
                templateTasks.Add(new TemplatePlannerTask
                {
                    TemplateId = officeTemplate.Id,
                    ActivityId = featureDev.Id,
                    StartTime = new TimeOnly(10, 0),
                    EndTime = new TimeOnly(13, 0),
                    IsBackground = false,
                    ImportanceId = highImportance.Id,
                    Location = "Office",
                    Notes = "Focused work time",
                    UserId = userId
                });
            }

            if (codeReview != null)
            {
                templateTasks.Add(new TemplatePlannerTask
                {
                    TemplateId = officeTemplate.Id,
                    ActivityId = codeReview.Id,
                    StartTime = new TimeOnly(14, 0),
                    EndTime = new TimeOnly(15, 30),
                    IsBackground = false,
                    ImportanceId = mediumImportance.Id,
                    Location = "Office",
                    UserId = userId
                });
            }
        }

        // Relaxed Weekend template tasks
        if (relaxedWeekendTemplate != null)
        {
            if (morningExercise != null)
            {
                templateTasks.Add(new TemplatePlannerTask
                {
                    TemplateId = relaxedWeekendTemplate.Id,
                    ActivityId = morningExercise.Id,
                    StartTime = new TimeOnly(10, 0),
                    EndTime = new TimeOnly(11, 0),
                    IsBackground = false,
                    ImportanceId = mediumImportance.Id,
                    Location = "Home",
                    UserId = userId
                });
            }

            if (houseCleaning != null)
            {
                templateTasks.Add(new TemplatePlannerTask
                {
                    TemplateId = relaxedWeekendTemplate.Id,
                    ActivityId = houseCleaning.Id,
                    StartTime = new TimeOnly(11, 0),
                    EndTime = new TimeOnly(13, 0),
                    IsBackground = false,
                    ImportanceId = mediumImportance.Id,
                    Location = "Home",
                    UserId = userId
                });
            }

            if (groceryShopping != null)
            {
                templateTasks.Add(new TemplatePlannerTask
                {
                    TemplateId = relaxedWeekendTemplate.Id,
                    ActivityId = groceryShopping.Id,
                    StartTime = new TimeOnly(13, 0),
                    EndTime = new TimeOnly(14, 30),
                    IsBackground = false,
                    ImportanceId = highImportance.Id,
                    Location = "Store",
                    UserId = userId
                });
            }

            if (sideProject != null)
            {
                templateTasks.Add(new TemplatePlannerTask
                {
                    TemplateId = relaxedWeekendTemplate.Id,
                    ActivityId = sideProject.Id,
                    StartTime = new TimeOnly(15, 0),
                    EndTime = new TimeOnly(17, 0),
                    IsBackground = false,
                    ImportanceId = optionalImportance.Id,
                    Location = "Home",
                    Notes = "Work on personal projects",
                    UserId = userId
                });
            }

            if (gaming != null)
            {
                templateTasks.Add(new TemplatePlannerTask
                {
                    TemplateId = relaxedWeekendTemplate.Id,
                    ActivityId = gaming.Id,
                    StartTime = new TimeOnly(19, 0),
                    EndTime = new TimeOnly(21, 0),
                    IsBackground = false,
                    ImportanceId = optionalImportance.Id,
                    Location = "Home",
                    Notes = "Relaxation time",
                    UserId = userId
                });
            }

            if (watchMovie != null)
            {
                templateTasks.Add(new TemplatePlannerTask
                {
                    TemplateId = relaxedWeekendTemplate.Id,
                    ActivityId = watchMovie.Id,
                    StartTime = new TimeOnly(21, 0),
                    EndTime = new TimeOnly(23, 0),
                    IsBackground = false,
                    ImportanceId = optionalImportance.Id,
                    Location = "Home",
                    UserId = userId
                });
            }

            if (meditation != null)
            {
                templateTasks.Add(new TemplatePlannerTask
                {
                    TemplateId = relaxedWeekendTemplate.Id,
                    ActivityId = meditation.Id,
                    StartTime = new TimeOnly(23, 0),
                    EndTime = new TimeOnly(23, 30),
                    IsBackground = false,
                    ImportanceId = mediumImportance.Id,
                    Location = "Home",
                    UserId = userId
                });
            }
        }

        await dbContext.TemplatePlannerTasks.AddRangeAsync(templateTasks);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} template planner tasks for user {UserId}", templateTasks.Count, userId);
    }
}
