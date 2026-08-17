using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// Fixtures shared by the to-do-list and routine endpoint tests (TEST-4). Every item ultimately needs
/// an <c>Activity</c>, so activity seeding is delegated to <see cref="PlanningTestSeedHelper"/> rather
/// than duplicated here.
/// </summary>
public static class TodoListTestSeedHelper
{
    public const long TestUserId = FakeLoggedUserService.TestUserId;

    public static async Task<long> SeedTodoListCategoryAsync(
        DbContext db, long userId = TestUserId, string? name = null, CancellationToken ct = default)
    {
        var entity = new TodoListCategory
        {
            UserId = userId,
            Name = name ?? $"Category-{Guid.NewGuid():N}",
            Color = "#abcdef"
        };
        db.Set<TodoListCategory>().Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public static async Task<long> SeedTodoListAsync(
        DbContext db, long userId = TestUserId, string? name = null, long? categoryId = null, CancellationToken ct = default)
    {
        var entity = new TodoList
        {
            UserId = userId,
            Name = name ?? $"List-{Guid.NewGuid():N}",
            CategoryId = categoryId
        };
        db.Set<TodoList>().Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public static async Task<long> SeedTaskPriorityAsync(
        DbContext db, int priority, long userId = TestUserId, string? text = null, CancellationToken ct = default)
    {
        var entity = new TaskPriority
        {
            UserId = userId,
            Text = text ?? $"Priority {priority}-{Guid.NewGuid():N}",
            Color = "#abcdef",
            Priority = priority
        };
        db.Set<TaskPriority>().Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public static async Task<long> SeedTodoListItemAsync(
        DbContext db, long activityId, long taskPriorityId, long todoListId,
        long userId = TestUserId, int? totalCount = null, int? doneCount = null, bool isDone = false,
        List<TodoListStep>? steps = null, CancellationToken ct = default)
    {
        var entity = new TodoListItem
        {
            UserId = userId,
            ActivityId = activityId,
            TaskPriorityId = taskPriorityId,
            TodoListId = todoListId,
            TotalCount = totalCount,
            DoneCount = doneCount,
            IsDone = isDone,
            DisplayOrder = 1000,
            Steps = steps ?? []
        };
        db.Set<TodoListItem>().Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public static async Task<long> SeedRoutineTimePeriodAsync(
        DbContext db, long userId = TestUserId, string? text = null, int lengthInDays = 7,
        int streakThreshold = 90, int streakGraceDays = 0, int resetAnchorDay = 1, int historyDepth = 16,
        int? reminderLeadDays = null, CancellationToken ct = default)
    {
        var entity = new RoutineTimePeriod
        {
            UserId = userId,
            Text = text ?? $"Period-{Guid.NewGuid():N}",
            Color = "#abcdef",
            LengthInDays = lengthInDays,
            StreakThreshold = streakThreshold,
            StreakGraceDays = streakGraceDays,
            ResetAnchorDay = resetAnchorDay,
            HistoryDepth = historyDepth,
            ReminderLeadDays = reminderLeadDays
        };
        db.Set<RoutineTimePeriod>().Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public static async Task<long> SeedRoutineTodoListAsync(
        DbContext db, long activityId, long timePeriodId, long userId = TestUserId,
        int? totalCount = null, int? doneCount = null, bool isDone = false,
        List<TodoListStep>? steps = null, CancellationToken ct = default)
    {
        var entity = new RoutineTodoList
        {
            UserId = userId,
            ActivityId = activityId,
            TimePeriodId = timePeriodId,
            TotalCount = totalCount,
            DoneCount = doneCount,
            IsDone = isDone,
            DisplayOrder = 1000,
            Steps = steps ?? []
        };
        db.Set<RoutineTodoList>().Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public static async Task<long> SeedRoutinePeriodCompletionAsync(
        DbContext db, long timePeriodId, DateOnly periodStart, DateOnly periodEnd,
        int completedCount = 1, int totalCount = 1, CancellationToken ct = default)
    {
        var entity = new RoutinePeriodCompletion
        {
            TimePeriodId = timePeriodId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            CompletedCount = completedCount,
            TotalCount = totalCount
        };
        db.Set<RoutinePeriodCompletion>().Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.Id;
    }
}
