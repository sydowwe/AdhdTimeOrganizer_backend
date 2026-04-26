using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence.seeder.@interface;
using AdhdTimeOrganizer.infrastructure.settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AdhdTimeOrganizer.infrastructure.persistence.seeder.dev;

public class TodoListItemSeeder(
    AppDbContext dbContext,
    IOptions<TodoListSettings> settings,
    ILogger<TodoListItemSeeder> logger) : IScopedService, IDevDatabaseSeeder
{
    public string SeederName => "TodoListItem";
    public int Order => 10;

    public async Task TruncateTable()
    {
        await dbContext.TruncateTableAsync<TodoListItem>();
    }


    public async Task SeedForUser(long userId)
    {
        // Note: TruncateTable already removed existing data for this user

        // Get activities and task priorities for this user
        var activities = await dbContext.Activities
            .Where(a => a.UserId == userId)
            .ToListAsync();

        var taskPriorities = await dbContext.TaskPriorities
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

        // Get or create default TodoList for this user
        var defaultTodoList = await dbContext.TodoLists
            .FirstOrDefaultAsync(tl => tl.UserId == userId && tl.Name == "Default");
        if (defaultTodoList == null)
        {
            defaultTodoList = new TodoList { Name = "Default", UserId = userId };
            await dbContext.TodoLists.AddAsync(defaultTodoList);
            await dbContext.SaveChangesAsync();
        }

        var todoLists = new List<TodoListItem>();

        // Get initial display order for each priority (simulate GetNextDisplayOrder logic)
        var lastOrderToday = await dbContext.TodoListItems
            .Where(tl => tl.UserId == userId && tl.TaskPriorityId == (todayPriority != null ? todayPriority.Id : 0))
            .MinAsync(tl => (int?)tl.DisplayOrder) ?? 0;
        var nextOrderToday = lastOrderToday != 0 ? lastOrderToday - settings.Value.DisplayOrderGap : settings.Value.DisplayOrderStart;

        var lastOrderThisWeek = await dbContext.TodoListItems
            .Where(tl => tl.UserId == userId && tl.TaskPriorityId == (thisWeekPriority != null ? thisWeekPriority.Id : 0))
            .MinAsync(tl => (int?)tl.DisplayOrder) ?? 0;
        var nextOrderThisWeek = lastOrderThisWeek != 0 ? lastOrderThisWeek - settings.Value.DisplayOrderGap : settings.Value.DisplayOrderStart;

        var lastOrderThisMonth = await dbContext.TodoListItems
            .Where(tl => tl.UserId == userId && tl.TaskPriorityId == (thisMonthPriority != null ? thisMonthPriority.Id : 0))
            .MinAsync(tl => (int?)tl.DisplayOrder) ?? 0;
        var nextOrderThisMonth = lastOrderThisMonth != 0 ? lastOrderThisMonth - settings.Value.DisplayOrderGap : settings.Value.DisplayOrderStart;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Today tasks
        if (todayPriority != null && bugFixingActivity != null)
        {
            todoLists.Add(new TodoListItem
            {
                ActivityId = bugFixingActivity.Id,
                TaskPriorityId = todayPriority.Id,
                TodoListId = defaultTodoList.Id,
                IsDone = false,
                DoneCount = 0,
                TotalCount = 3,
                DisplayOrder = nextOrderToday,
                UserId = userId,
                SuggestedTime = new IntTime(10, 0),
                Note = "Fix login timeout and null ref on dashboard",
                DueDate = today,
                DueTime = new TimeOnly(17, 0),
                Steps =
                [
                    new TodoListStep { Name = "Reproduce login timeout locally", Order = 1, IsDone = true },
                    new TodoListStep { Name = "Fix null ref on dashboard", Order = 2, IsDone = false },
                    new TodoListStep { Name = "Write regression tests", Order = 3, IsDone = false }
                ]
            });
            nextOrderToday -= settings.Value.DisplayOrderGap;
        }

        if (todayPriority != null && groceryActivity != null)
        {
            todoLists.Add(new TodoListItem
            {
                ActivityId = groceryActivity.Id,
                TaskPriorityId = todayPriority.Id,
                TodoListId = defaultTodoList.Id,
                IsDone = false,
                DisplayOrder = nextOrderToday,
                UserId = userId,
                DueDate = today,
                Steps =
                [
                    new TodoListStep { Name = "Milk & eggs", Order = 1, IsDone = false },
                    new TodoListStep { Name = "Vegetables", Order = 2, IsDone = false },
                    new TodoListStep { Name = "Chicken", Order = 3, IsDone = true }
                ]
            });
            nextOrderToday -= settings.Value.DisplayOrderGap;
        }

        if (todayPriority != null && exerciseActivity != null)
        {
            todoLists.Add(new TodoListItem
            {
                ActivityId = exerciseActivity.Id,
                TaskPriorityId = todayPriority.Id,
                TodoListId = defaultTodoList.Id,
                IsDone = true,
                DisplayOrder = nextOrderToday,
                UserId = userId,
                SuggestedTime = new IntTime(7, 0)
            });
            nextOrderToday -= settings.Value.DisplayOrderGap;
        }

        // This week tasks
        if (thisWeekPriority != null && codeReviewActivity != null)
        {
            todoLists.Add(new TodoListItem
            {
                ActivityId = codeReviewActivity.Id,
                TaskPriorityId = thisWeekPriority.Id,
                TodoListId = defaultTodoList.Id,
                IsDone = false,
                DoneCount = 2,
                TotalCount = 5,
                DisplayOrder = nextOrderThisWeek,
                UserId = userId,
                Note = "Auth module PRs from the team",
                DueDate = today.AddDays(7 - (int)today.DayOfWeek)
            });
            nextOrderThisWeek -= settings.Value.DisplayOrderGap;
        }

        if (thisWeekPriority != null && featureDevActivity != null)
        {
            todoLists.Add(new TodoListItem
            {
                ActivityId = featureDevActivity.Id,
                TaskPriorityId = thisWeekPriority.Id,
                TodoListId = defaultTodoList.Id,
                IsDone = false,
                DoneCount = 1,
                TotalCount = 4,
                DisplayOrder = nextOrderThisWeek,
                UserId = userId,
                SuggestedTime = new IntTime(9, 0),
                Steps =
                [
                    new TodoListStep { Name = "Design settings screen layout", Order = 1, IsDone = true },
                    new TodoListStep { Name = "Implement notification toggle", Order = 2, IsDone = false },
                    new TodoListStep { Name = "Hook up to backend API", Order = 3, IsDone = false }
                ]
            });
            nextOrderThisWeek -= settings.Value.DisplayOrderGap;
        }

        if (thisWeekPriority != null && onlineCourseActivity != null)
        {
            todoLists.Add(new TodoListItem
            {
                ActivityId = onlineCourseActivity.Id,
                TaskPriorityId = thisWeekPriority.Id,
                TodoListId = defaultTodoList.Id,
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
            todoLists.Add(new TodoListItem
            {
                ActivityId = meditationActivity.Id,
                TaskPriorityId = thisMonthPriority.Id,
                TodoListId = defaultTodoList.Id,
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
            todoLists.Add(new TodoListItem
            {
                ActivityId = sideProjectActivity.Id,
                TaskPriorityId = thisMonthPriority.Id,
                TodoListId = defaultTodoList.Id,
                IsDone = false,
                DisplayOrder = nextOrderThisMonth,
                UserId = userId,
                Note = "Implement export to PDF feature",
                DueDate = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month))
            });
            nextOrderThisMonth -= settings.Value.DisplayOrderGap;
        }

        await dbContext.TodoListItems.AddRangeAsync(todoLists);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} todo lists for user {UserId}", todoLists.Count, userId);
    }
}
