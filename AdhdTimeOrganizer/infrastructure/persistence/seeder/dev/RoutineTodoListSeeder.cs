using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;
using AdhdTimeOrganizer.infrastructure.settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.dev;

public class RoutineTodoListSeeder(
    AppCommandDbContext dbContext,
    IOptions<TodoListSettings> settings,
    ILogger<RoutineTodoListSeeder> logger) : IScopedService, IDevDatabaseSeeder
{
    public string SeederName => "RoutineTodoList";
    public int Order => 11;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableAsync<RoutineTodoList>();
    }


    public async Task SeedForUser(long userId)
    {
        // Note: TruncateTable already removed existing data for this user

        // Get activities and routine time periods for this user
        var activities = await dbContext.Activities
            .Where(a => a.UserId == userId)
            .ToListAsync();

        var timePeriods = await dbContext.RoutineTimePeriods
            .Where(rtp => rtp.UserId == userId)
            .OrderBy(rtp => rtp.LengthInDays)
            .ToListAsync();

        if (!activities.Any())
        {
            logger.LogWarning("No activities found for user {UserId}. Skipping routine todo list seeding.", userId);
            return;
        }

        if (!timePeriods.Any())
        {
            logger.LogWarning("No routine time periods found for user {UserId}. Skipping routine todo list seeding.", userId);
            return;
        }

        // Get time periods by text (from UserDefaultsService)
        var dailyPeriod = timePeriods.FirstOrDefault(tp => tp.Text == "Daily");
        var weeklyPeriod = timePeriods.FirstOrDefault(tp => tp.Text == "Weekly");
        var monthlyPeriod = timePeriods.FirstOrDefault(tp => tp.Text == "Monthly");

        // Get specific activities for realistic routine todos
        var exerciseActivity = activities.FirstOrDefault(a => a.Name == "Morning Exercise");
        var meditationActivity = activities.FirstOrDefault(a => a.Name == "Meditation");
        var journalingActivity = activities.FirstOrDefault(a => a.Name == "Journaling");
        var mealPrepActivity = activities.FirstOrDefault(a => a.Name == "Meal Preparation");
        var laundryActivity = activities.FirstOrDefault(a => a.Name == "Laundry");
        var houseCleaningActivity = activities.FirstOrDefault(a => a.Name == "House Cleaning");
        var groceryActivity = activities.FirstOrDefault(a => a.Name == "Grocery Shopping");
        var videoCallParentsActivity = activities.FirstOrDefault(a => a.Name == "Video Call Parents");
        var readBookActivity = activities.FirstOrDefault(a => a.Name == "Read Technical Book");

        var routineTodoLists = new List<RoutineTodoList>();

        // Get initial display order for each time period (simulate GetNextDisplayOrder logic)
        var lastOrderDaily = await dbContext.RoutineTodoLists
            .Where(rtl => rtl.UserId == userId && rtl.TimePeriodId == (dailyPeriod != null ? dailyPeriod.Id : 0))
            .MinAsync(rtl => (int?)rtl.DisplayOrder) ?? 0;
        var nextOrderDaily = lastOrderDaily != 0 ? lastOrderDaily - settings.Value.DisplayOrderGap : settings.Value.DisplayOrderStart;

        var lastOrderWeekly = await dbContext.RoutineTodoLists
            .Where(rtl => rtl.UserId == userId && rtl.TimePeriodId == (weeklyPeriod != null ? weeklyPeriod.Id : 0))
            .MinAsync(rtl => (int?)rtl.DisplayOrder) ?? 0;
        var nextOrderWeekly = lastOrderWeekly != 0 ? lastOrderWeekly - settings.Value.DisplayOrderGap : settings.Value.DisplayOrderStart;

        var lastOrderMonthly = await dbContext.RoutineTodoLists
            .Where(rtl => rtl.UserId == userId && rtl.TimePeriodId == (monthlyPeriod != null ? monthlyPeriod.Id : 0))
            .MinAsync(rtl => (int?)rtl.DisplayOrder) ?? 0;
        var nextOrderMonthly = lastOrderMonthly != 0 ? lastOrderMonthly - settings.Value.DisplayOrderGap : settings.Value.DisplayOrderStart;

        // Daily routines
        if (dailyPeriod != null && exerciseActivity != null)
        {
            routineTodoLists.Add(new RoutineTodoList
            {
                ActivityId = exerciseActivity.Id,
                TimePeriodId = dailyPeriod.Id,
                IsDone = true,
                DisplayOrder = nextOrderDaily,
                UserId = userId
            });
            nextOrderDaily -= settings.Value.DisplayOrderGap;
        }

        if (dailyPeriod != null && meditationActivity != null)
        {
            routineTodoLists.Add(new RoutineTodoList
            {
                ActivityId = meditationActivity.Id,
                TimePeriodId = dailyPeriod.Id,
                IsDone = false,
                DisplayOrder = nextOrderDaily,
                UserId = userId
            });
            nextOrderDaily -= settings.Value.DisplayOrderGap;
        }

        if (dailyPeriod != null && journalingActivity != null)
        {
            routineTodoLists.Add(new RoutineTodoList
            {
                ActivityId = journalingActivity.Id,
                TimePeriodId = dailyPeriod.Id,
                IsDone = false,
                DisplayOrder = nextOrderDaily,
                UserId = userId
            });
            nextOrderDaily -= settings.Value.DisplayOrderGap;
        }

        if (dailyPeriod != null && mealPrepActivity != null)
        {
            routineTodoLists.Add(new RoutineTodoList
            {
                ActivityId = mealPrepActivity.Id,
                TimePeriodId = dailyPeriod.Id,
                IsDone = true,
                DoneCount = 3,
                TotalCount = 3,
                DisplayOrder = nextOrderDaily,
                UserId = userId
            });
            nextOrderDaily -= settings.Value.DisplayOrderGap;
        }

        // Weekly routines
        if (weeklyPeriod != null && laundryActivity != null)
        {
            routineTodoLists.Add(new RoutineTodoList
            {
                ActivityId = laundryActivity.Id,
                TimePeriodId = weeklyPeriod.Id,
                IsDone = false,
                DoneCount = 0,
                TotalCount = 2,
                DisplayOrder = nextOrderWeekly,
                UserId = userId
            });
            nextOrderWeekly -= settings.Value.DisplayOrderGap;
        }

        if (weeklyPeriod != null && houseCleaningActivity != null)
        {
            routineTodoLists.Add(new RoutineTodoList
            {
                ActivityId = houseCleaningActivity.Id,
                TimePeriodId = weeklyPeriod.Id,
                IsDone = false,
                DoneCount = 1,
                TotalCount = 4,
                DisplayOrder = nextOrderWeekly,
                UserId = userId
            });
            nextOrderWeekly -= settings.Value.DisplayOrderGap;
        }

        if (weeklyPeriod != null && groceryActivity != null)
        {
            routineTodoLists.Add(new RoutineTodoList
            {
                ActivityId = groceryActivity.Id,
                TimePeriodId = weeklyPeriod.Id,
                IsDone = true,
                DisplayOrder = nextOrderWeekly,
                UserId = userId
            });
            nextOrderWeekly -= settings.Value.DisplayOrderGap;
        }

        if (weeklyPeriod != null && videoCallParentsActivity != null)
        {
            routineTodoLists.Add(new RoutineTodoList
            {
                ActivityId = videoCallParentsActivity.Id,
                TimePeriodId = weeklyPeriod.Id,
                IsDone = false,
                DisplayOrder = nextOrderWeekly,
                UserId = userId
            });
            nextOrderWeekly -= settings.Value.DisplayOrderGap;
        }

        // Monthly routines
        if (monthlyPeriod != null && readBookActivity != null)
        {
            routineTodoLists.Add(new RoutineTodoList
            {
                ActivityId = readBookActivity.Id,
                TimePeriodId = monthlyPeriod.Id,
                IsDone = false,
                DoneCount = 1,
                TotalCount = 4,
                DisplayOrder = nextOrderMonthly,
                UserId = userId
            });
            nextOrderMonthly -= settings.Value.DisplayOrderGap;
        }

        await dbContext.RoutineTodoLists.AddRangeAsync(routineTodoLists);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} routine todo lists for user {UserId}", routineTodoLists.Count, userId);
    }
}
