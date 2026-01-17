using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence.extensions;
using AdhdTimeOrganizer.infrastructure.persistence.seeders;
using AdhdTimeOrganizer.infrastructure.settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.@default;

public class TodoListSeeder(
    AppCommandDbContext dbContext,
    IOptions<TodoListSettings> settings,
    ILogger<TodoListSeeder> logger) : IScopedService, IDevDatabaseSeeder
{
    public string SeederName => "TodoList";
    public int Order => 10;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableAsync<TodoList>();
    }


    public async Task SeedForUser(long userId)
    {
        // Note: TruncateTable already removed existing data for this user

        // Get activities and task priorities for this user
        var activities = await dbContext.Activities
            .Where(a => a.UserId == userId)
            .ToListAsync();

        var taskPriorities = await dbContext.TaskUrgencies
            .Where(tp => tp.UserId == userId)
            .OrderBy(tp => tp.Priority)
            .ToListAsync();

        if (!activities.Any())
        {
            logger.LogWarning("No activities found for user {UserId}. Skipping todo list seeding.", userId);
            return;
        }

        if (!taskPriorities.Any())
        {
            logger.LogWarning("No task priorities found for user {UserId}. Skipping todo list seeding.", userId);
            return;
        }

        // Get priorities by text (from UserDefaultsService)
        var todayPriority = taskPriorities.FirstOrDefault(tp => tp.Text == "Today");
        var thisWeekPriority = taskPriorities.FirstOrDefault(tp => tp.Text == "This week");
        var thisMonthPriority = taskPriorities.FirstOrDefault(tp => tp.Text == "This month");

        // Get some specific activities for realistic todos
        var codeReviewActivity = activities.FirstOrDefault(a => a.Name == "Code Review");
        var bugFixingActivity = activities.FirstOrDefault(a => a.Name == "Bug Fixing");
        var featureDevActivity = activities.FirstOrDefault(a => a.Name == "Feature Development");
        var groceryActivity = activities.FirstOrDefault(a => a.Name == "Grocery Shopping");
        var exerciseActivity = activities.FirstOrDefault(a => a.Name == "Morning Exercise");
        var onlineCourseActivity = activities.FirstOrDefault(a => a.Name == "Online Course");
        var meditationActivity = activities.FirstOrDefault(a => a.Name == "Meditation");
        var sideProjectActivity = activities.FirstOrDefault(a => a.Name == "Side Project");

        var todoLists = new List<TodoList>();

        // Get initial display order for each priority (simulate GetNextDisplayOrder logic)
        var lastOrderToday = await dbContext.TodoLists
            .Where(tl => tl.UserId == userId && tl.TaskPriorityId == (todayPriority != null ? todayPriority.Id : 0))
            .MinAsync(tl => (int?)tl.DisplayOrder) ?? 0;
        var nextOrderToday = lastOrderToday != 0 ? lastOrderToday - settings.Value.DisplayOrderGap : settings.Value.DisplayOrderStart;

        var lastOrderThisWeek = await dbContext.TodoLists
            .Where(tl => tl.UserId == userId && tl.TaskPriorityId == (thisWeekPriority != null ? thisWeekPriority.Id : 0))
            .MinAsync(tl => (int?)tl.DisplayOrder) ?? 0;
        var nextOrderThisWeek = lastOrderThisWeek != 0 ? lastOrderThisWeek - settings.Value.DisplayOrderGap : settings.Value.DisplayOrderStart;

        var lastOrderThisMonth = await dbContext.TodoLists
            .Where(tl => tl.UserId == userId && tl.TaskPriorityId == (thisMonthPriority != null ? thisMonthPriority.Id : 0))
            .MinAsync(tl => (int?)tl.DisplayOrder) ?? 0;
        var nextOrderThisMonth = lastOrderThisMonth != 0 ? lastOrderThisMonth - settings.Value.DisplayOrderGap : settings.Value.DisplayOrderStart;

        // Today tasks
        if (todayPriority != null && bugFixingActivity != null)
        {
            todoLists.Add(new TodoList
            {
                ActivityId = bugFixingActivity.Id,
                TaskPriorityId = todayPriority.Id,
                IsDone = false,
                DoneCount = 0,
                TotalCount = 3,
                DisplayOrder = nextOrderToday,
                UserId = userId
            });
            nextOrderToday -= settings.Value.DisplayOrderGap;
        }

        if (todayPriority != null && groceryActivity != null)
        {
            todoLists.Add(new TodoList
            {
                ActivityId = groceryActivity.Id,
                TaskPriorityId = todayPriority.Id,
                IsDone = false,
                DisplayOrder = nextOrderToday,
                UserId = userId
            });
            nextOrderToday -= settings.Value.DisplayOrderGap;
        }

        if (todayPriority != null && exerciseActivity != null)
        {
            todoLists.Add(new TodoList
            {
                ActivityId = exerciseActivity.Id,
                TaskPriorityId = todayPriority.Id,
                IsDone = true,
                DisplayOrder = nextOrderToday,
                UserId = userId
            });
            nextOrderToday -= settings.Value.DisplayOrderGap;
        }

        // This week tasks
        if (thisWeekPriority != null && codeReviewActivity != null)
        {
            todoLists.Add(new TodoList
            {
                ActivityId = codeReviewActivity.Id,
                TaskPriorityId = thisWeekPriority.Id,
                IsDone = false,
                DoneCount = 2,
                TotalCount = 5,
                DisplayOrder = nextOrderThisWeek,
                UserId = userId
            });
            nextOrderThisWeek -= settings.Value.DisplayOrderGap;
        }

        if (thisWeekPriority != null && featureDevActivity != null)
        {
            todoLists.Add(new TodoList
            {
                ActivityId = featureDevActivity.Id,
                TaskPriorityId = thisWeekPriority.Id,
                IsDone = false,
                DoneCount = 1,
                TotalCount = 4,
                DisplayOrder = nextOrderThisWeek,
                UserId = userId
            });
            nextOrderThisWeek -= settings.Value.DisplayOrderGap;
        }

        if (thisWeekPriority != null && onlineCourseActivity != null)
        {
            todoLists.Add(new TodoList
            {
                ActivityId = onlineCourseActivity.Id,
                TaskPriorityId = thisWeekPriority.Id,
                IsDone = false,
                DoneCount = 0,
                TotalCount = 2,
                DisplayOrder = nextOrderThisWeek,
                UserId = userId
            });
            nextOrderThisWeek -= settings.Value.DisplayOrderGap;
        }

        // This month tasks
        if (thisMonthPriority != null && meditationActivity != null)
        {
            todoLists.Add(new TodoList
            {
                ActivityId = meditationActivity.Id,
                TaskPriorityId = thisMonthPriority.Id,
                IsDone = false,
                DoneCount = 5,
                TotalCount = 20,
                DisplayOrder = nextOrderThisMonth,
                UserId = userId
            });
            nextOrderThisMonth -= settings.Value.DisplayOrderGap;
        }

        if (thisMonthPriority != null && sideProjectActivity != null)
        {
            todoLists.Add(new TodoList
            {
                ActivityId = sideProjectActivity.Id,
                TaskPriorityId = thisMonthPriority.Id,
                IsDone = false,
                DisplayOrder = nextOrderThisMonth,
                UserId = userId
            });
            nextOrderThisMonth -= settings.Value.DisplayOrderGap;
        }

        await dbContext.TodoLists.AddRangeAsync(todoLists);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} todo lists for user {UserId}", todoLists.Count, userId);
    }
}
