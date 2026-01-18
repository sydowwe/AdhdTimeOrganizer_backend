using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.dev;

public class PlannerTaskSeeder(
    AppCommandDbContext dbContext,
    ILogger<PlannerTaskSeeder> logger) : IDevDatabaseSeeder, IScopedService
{
    public string SeederName => "PlannerTask";
    public int Order => 13;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableAsync<PlannerTask>();
    }

    public async Task SeedForUser(long userId)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);

        var calendars = await dbContext.Calendars
            .Where(c => c.UserId == userId && c.Date >= today)
            .OrderBy(c => c.Date)
            .Take(7) // Get next 7 days
            .ToListAsync();

        var activities = await dbContext.Activities
            .Where(a => a.UserId == userId)
            .ToListAsync();

        var todoLists = await dbContext.TodoLists
            .Where(tl => tl.UserId == userId)
            .ToListAsync();

        var importances = await dbContext.TaskImportances
            .Where(i => i.UserId == userId)
            .ToListAsync();

        if (!calendars.Any())
        {
            logger.LogWarning("No calendars found for user {UserId}. Skipping planner task seeding.", userId);
            return;
        }

        if (!activities.Any())
        {
            logger.LogWarning("No activities found for user {UserId}. Skipping planner task seeding.", userId);
            return;
        }

        // Get specific activities
        var morningExercise = activities.FirstOrDefault(a => a.Name == "Morning Exercise");
        var dailyStandup = activities.FirstOrDefault(a => a.Name == "Daily Standup");
        var featureDev = activities.FirstOrDefault(a => a.Name == "Feature Development");
        var codeReview = activities.FirstOrDefault(a => a.Name == "Code Review");
        var bugFixing = activities.FirstOrDefault(a => a.Name == "Bug Fixing");
        var mealPrep = activities.FirstOrDefault(a => a.Name == "Meal Preparation");
        var meditation = activities.FirstOrDefault(a => a.Name == "Meditation");
        var sleepRoutine = activities.FirstOrDefault(a => a.Name == "Sleep Routine");
        var familyDinner = activities.FirstOrDefault(a => a.Name == "Family Dinner");
        var onlineCourse = activities.FirstOrDefault(a => a.Name == "Online Course");
        var sideProject = activities.FirstOrDefault(a => a.Name == "Side Project");
        var gaming = activities.FirstOrDefault(a => a.Name == "Gaming Session");
        var sprintPlanning = activities.FirstOrDefault(a => a.Name == "Sprint Planning");
        var laundry = activities.FirstOrDefault(a => a.Name == "Laundry");
        var houseCleaning = activities.FirstOrDefault(a => a.Name == "House Cleaning");
        var groceryShopping = activities.FirstOrDefault(a => a.Name == "Grocery Shopping");
        var watchMovie = activities.FirstOrDefault(a => a.Name == "Watch Movie/Series");

        // Get importance levels
        var criticalImportance = importances.FirstOrDefault(i => i.Importance == 999);
        var highImportance = importances.FirstOrDefault(i => i.Importance == 888);
        var mediumImportance = importances.FirstOrDefault(i => i.Importance == 777);
        var optionalImportance = importances.FirstOrDefault(i => i.Importance == 666);
        if (criticalImportance == null || highImportance == null || mediumImportance == null || optionalImportance == null)
        {
            throw new InvalidOperationException("Critical importance levels are missing in the database.");
        }

        // Get a to-do list to link (optional)
        var bugFixingTodo = todoLists.FirstOrDefault(tl => tl.ActivityId == bugFixing?.Id);

        var plannerTasks = new List<PlannerTask>();

        // Seed tasks for each calendar day
        foreach (var calendar in calendars)
        {
            var isWeekend = calendar.IsWeekend;
            // Update calendar fields
            calendar.Label = isWeekend ? null : (calendar.Date.Day % 2 == 0 ? "HomeOffice" : "Office");
            calendar.WakeUpTime = isWeekend ? new TimeOnly(9, 0) : new TimeOnly(7, 0);
            calendar.BedTime = new TimeOnly(23, 0);
            calendar.Weather = calendar.Date.Month switch
            {
                12 or 1 or 2 => "Cold ❄️",
                3 or 4 or 5 => "Mild 🌤️",
                6 or 7 or 8 => "Hot ☀️",
                _ => "Cool 🍂"
            };
            calendar.Notes = isWeekend ? "Weekend - relaxed schedule" : "Workday - stay focused";

            // Morning routine (every day)
            if (morningExercise != null)
            {
                plannerTasks.Add(new PlannerTask
                {
                    CalendarId = calendar.Id,
                    ActivityId = morningExercise.Id,
                    StartTime = isWeekend ? new TimeOnly(9, 0) : new TimeOnly(7, 30),
                    EndTime = isWeekend ? new TimeOnly(10, 0) : new TimeOnly(8, 30),
                    IsBackground = false,

                    ImportanceId = mediumImportance.Id,
                    IsDone = false,
                    Status = PlannerTaskStatus.NotStarted,
                    Location = "Home",
                    Notes = "Morning workout",
                    IsFromTemplate = false,
                    UserId = userId
                });
            }

            if (!isWeekend)
            {
                // Workday tasks
                if (dailyStandup != null)
                {
                    plannerTasks.Add(new PlannerTask
                    {
                        CalendarId = calendar.Id,
                        ActivityId = dailyStandup.Id,
                        StartTime = new TimeOnly(9, 0),
                        EndTime = new TimeOnly(9, 15),
                        IsBackground = false,
                        ImportanceId = criticalImportance.Id,
                        IsDone = false,
                        Status = PlannerTaskStatus.NotStarted,
                        Location = "Home",
                        Notes = "Daily team sync - Mandatory",
                        IsFromTemplate = false,
                        UserId = userId
                    });
                }

                if (calendar.Date.DayOfWeek == DayOfWeek.Monday && sprintPlanning != null)
                {
                    plannerTasks.Add(new PlannerTask
                    {
                        CalendarId = calendar.Id,
                        ActivityId = sprintPlanning.Id,
                        StartTime = new TimeOnly(10, 0),
                        EndTime = new TimeOnly(11, 30),
                        IsBackground = false,
                        ImportanceId = criticalImportance.Id,
                        IsDone = false,
                        Status = PlannerTaskStatus.NotStarted,
                        Location = "Meeting Room",
                        Notes = "Weekly sprint planning",
                        IsFromTemplate = false,
                        UserId = userId
                    });
                }

                if (featureDev != null)
                {
                    plannerTasks.Add(new PlannerTask
                    {
                        CalendarId = calendar.Id,
                        ActivityId = featureDev.Id,
                        StartTime = calendar.Date.DayOfWeek == DayOfWeek.Monday ? new TimeOnly(11, 30) : new TimeOnly(9, 30),
                        EndTime = new TimeOnly(13, 0),
                        IsBackground = false,
                        ImportanceId = highImportance.Id,
                        IsDone = false,
                        Status = PlannerTaskStatus.NotStarted,
                        Location = "Home",
                        Notes = "Deep work session - new features",
                        IsFromTemplate = false,
                        UserId = userId
                    });
                }

                if (bugFixing != null)
                {
                    plannerTasks.Add(new PlannerTask
                    {
                        CalendarId = calendar.Id,
                        ActivityId = bugFixing.Id,
                        TodolistId = bugFixingTodo?.Id,
                        StartTime = new TimeOnly(14, 0),
                        EndTime = new TimeOnly(16, 0),
                        IsBackground = false,
                        ImportanceId = highImportance.Id,
                        IsDone = false,
                        Status = PlannerTaskStatus.NotStarted,
                        Location = "Home",
                        Notes = "Fix critical bugs",
                        IsFromTemplate = false,
                        UserId = userId
                    });
                }

                if (codeReview != null)
                {
                    plannerTasks.Add(new PlannerTask
                    {
                        CalendarId = calendar.Id,
                        ActivityId = codeReview.Id,
                        StartTime = new TimeOnly(16, 0),
                        EndTime = new TimeOnly(17, 0),
                        IsBackground = false,

                        ImportanceId = mediumImportance.Id,
                        IsDone = false,
                        Status = PlannerTaskStatus.NotStarted,
                        Location = "Home",
                        Notes = "Review team PRs",
                        IsFromTemplate = false,
                        UserId = userId
                    });
                }

                if (laundry != null && calendar.Date.DayOfWeek == DayOfWeek.Wednesday)
                {
                    plannerTasks.Add(new PlannerTask
                    {
                        CalendarId = calendar.Id,
                        ActivityId = laundry.Id,
                        StartTime = new TimeOnly(17, 0),
                        EndTime = new TimeOnly(18, 0),
                        IsBackground = true,
                        ImportanceId = optionalImportance.Id,
                        IsDone = false,
                        Status = PlannerTaskStatus.NotStarted,
                        Location = "Home",
                        Notes = "Do some laundry",
                        IsFromTemplate = false,
                        UserId = userId
                    });
                }
            }
            else
            {
                // Weekend tasks
                if (houseCleaning != null && calendar.Date.DayOfWeek == DayOfWeek.Saturday)
                {
                    plannerTasks.Add(new PlannerTask
                    {
                        CalendarId = calendar.Id,
                        ActivityId = houseCleaning.Id,
                        StartTime = new TimeOnly(10, 0),
                        EndTime = new TimeOnly(12, 0),
                        IsBackground = false,
                        ImportanceId = mediumImportance.Id,
                        IsDone = false,
                        Status = PlannerTaskStatus.NotStarted,
                        Location = "Home",
                        Notes = "Weekly cleaning",
                        IsFromTemplate = false,
                        UserId = userId
                    });
                }

                if (groceryShopping != null && calendar.Date.DayOfWeek == DayOfWeek.Saturday)
                {
                    plannerTasks.Add(new PlannerTask
                    {
                        CalendarId = calendar.Id,
                        ActivityId = groceryShopping.Id,
                        StartTime = new TimeOnly(13, 0),
                        EndTime = new TimeOnly(14, 30),
                        IsBackground = false,
                        ImportanceId = highImportance.Id,
                        IsDone = false,
                        Status = PlannerTaskStatus.NotStarted,
                        Location = "Supermarket",
                        Notes = "Buy food for the week",
                        IsFromTemplate = false,
                        UserId = userId
                    });
                }

                if (onlineCourse != null)
                {
                    plannerTasks.Add(new PlannerTask
                    {
                        CalendarId = calendar.Id,
                        ActivityId = onlineCourse.Id,
                        StartTime = isWeekend && calendar.Date.DayOfWeek == DayOfWeek.Saturday ? new TimeOnly(15, 0) : new TimeOnly(11, 0),
                        EndTime = isWeekend && calendar.Date.DayOfWeek == DayOfWeek.Saturday ? new TimeOnly(16, 30) : new TimeOnly(12, 30),
                        IsBackground = false,

                        ImportanceId = mediumImportance.Id,
                        IsDone = false,
                        Status = PlannerTaskStatus.NotStarted,
                        Location = "Home",
                        Notes = "Learning time",
                        IsFromTemplate = false,
                        UserId = userId
                    });
                }

                if (sideProject != null)
                {
                    plannerTasks.Add(new PlannerTask
                    {
                        CalendarId = calendar.Id,
                        ActivityId = sideProject.Id,
                        StartTime = new TimeOnly(14, 0),
                        EndTime = new TimeOnly(17, 0),
                        IsBackground = false,

                        ImportanceId = optionalImportance.Id,
                        IsDone = false,
                        Status = PlannerTaskStatus.NotStarted,
                        Location = "Home",
                        Notes = "Personal project work",
                        IsFromTemplate = false,
                        UserId = userId
                    });
                }

                if (gaming != null)
                {
                    plannerTasks.Add(new PlannerTask
                    {
                        CalendarId = calendar.Id,
                        ActivityId = gaming.Id,
                        StartTime = new TimeOnly(19, 0),
                        EndTime = new TimeOnly(21, 0),
                        IsBackground = false,

                        ImportanceId = optionalImportance.Id,
                        IsDone = false,
                        Status = PlannerTaskStatus.NotStarted,
                        Location = "Home",
                        Notes = "Gaming session for fun",
                        IsFromTemplate = false,
                        UserId = userId
                    });
                }

                if (watchMovie != null && calendar.Date.DayOfWeek == DayOfWeek.Sunday)
                {
                    plannerTasks.Add(new PlannerTask
                    {
                        CalendarId = calendar.Id,
                        ActivityId = watchMovie.Id,
                        StartTime = new TimeOnly(20, 0),
                        EndTime = new TimeOnly(22, 0),
                        IsBackground = false,
                        ImportanceId = optionalImportance.Id,
                        IsDone = false,
                        Status = PlannerTaskStatus.NotStarted,
                        Location = "Home",
                        Notes = "Relaxing with a movie",
                        IsFromTemplate = false,
                        UserId = userId
                    });
                }
            }

            // Evening routine (every day)
            if (familyDinner != null)
            {
                plannerTasks.Add(new PlannerTask
                {
                    CalendarId = calendar.Id,
                    ActivityId = familyDinner.Id,
                    StartTime = new TimeOnly(18, 30),
                    EndTime = new TimeOnly(19, 30),
                    IsBackground = false,
                    ImportanceId = criticalImportance.Id,
                    IsDone = false,
                    Status = PlannerTaskStatus.NotStarted,
                    Location = "Home",
                    Notes = "Family time - Important",
                    IsFromTemplate = false,
                    UserId = userId
                });
            }

            if (meditation != null)
            {
                plannerTasks.Add(new PlannerTask
                {
                    CalendarId = calendar.Id,
                    ActivityId = meditation.Id,
                    StartTime = new TimeOnly(21, 30),
                    EndTime = new TimeOnly(22, 0),
                    IsBackground = false,

                    ImportanceId = mediumImportance.Id,
                    IsDone = false,
                    Status = PlannerTaskStatus.NotStarted,
                    Location = "Home",
                    Notes = "Evening meditation",
                    IsFromTemplate = false,
                    UserId = userId
                });
            }

            if (sleepRoutine != null)
            {
                plannerTasks.Add(new PlannerTask
                {
                    CalendarId = calendar.Id,
                    ActivityId = sleepRoutine.Id,
                    StartTime = new TimeOnly(22, 30),
                    EndTime = new TimeOnly(23, 0),
                    IsBackground = false,
                    ImportanceId = highImportance.Id,
                    IsDone = false,
                    Status = PlannerTaskStatus.NotStarted,
                    Location = "Home",
                    Notes = "Wind down for sleep",
                    IsFromTemplate = false,
                    UserId = userId
                });
            }
        }

        await dbContext.PlannerTasks.AddRangeAsync(plannerTasks);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} planner tasks for user {UserId} across {DayCount} days",
            plannerTasks.Count, userId, calendars.Count);
    }
}