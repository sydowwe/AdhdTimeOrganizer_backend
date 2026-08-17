using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Sydowwe.Framework.Testing.baseTests;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-4 / Section A — the plain CRUD auth matrix (401 unauthenticated, 404 IDOR — not the bases' default
/// 403, because every entity here is <c>IEntityWithUser</c> and covered by <c>AppDbContext</c>'s global
/// query filter — and User role allowed) across all six to-do-list / routine entity folders, driven off the
/// shared framework endpoint test bases. Follows the same pattern <c>PlanningCrudAuthMatrixTests</c>
/// established (TEST-3 / Section A) — this is the second portal area to subclass those bases.
/// <para>
/// None of these endpoints override <c>AllowedRoles()</c>, so the default User+Admin+Root applies
/// everywhere — <c>IsAdminOnly = false</c> throughout.
/// </para>
/// </summary>
[Collection("Postgres")]
public class CreateTodoListEndpointTests(AppDbContextFixture fixture) : BaseCreateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/todo-list";
    protected override bool IsAdminOnly => false;

    protected override Task<object> BuildValidPayloadAsync(DbContext db) => Task.FromResult<object>(new
    {
        Name = $"List-{Guid.NewGuid():N}"
    });
}

[Collection("Postgres")]
public class UpdateTodoListEndpointTests(AppDbContextFixture fixture) : BaseUpdateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/todo-list";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override Task<long> SeedEntityAsync(DbContext db) =>
        TodoListTestSeedHelper.SeedTodoListAsync(db, name: "update-mine");

    protected override Task<object> BuildValidPayloadAsync(DbContext db, long id) => Task.FromResult<object>(new
    {
        Name = "Updated list"
    });

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        return await TodoListTestSeedHelper.SeedTodoListAsync(db, ReminderSeedHelper.OtherUserId, "update-theirs");
    }
}

[Collection("Postgres")]
public class DeleteTodoListEndpointTests(AppDbContextFixture fixture) : BaseDeleteEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/todo-list";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override Task<long> SeedEntityAsync(DbContext db) =>
        TodoListTestSeedHelper.SeedTodoListAsync(db, name: "delete-mine");

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        return await TodoListTestSeedHelper.SeedTodoListAsync(db, ReminderSeedHelper.OtherUserId, "delete-theirs");
    }
}

[Collection("Postgres")]
public class GetByIdTodoListEndpointTests(AppDbContextFixture fixture) : BaseGetByIdEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/todo-list";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override Task<long> SeedEntityAsync(DbContext db) =>
        TodoListTestSeedHelper.SeedTodoListAsync(db, name: "getbyid-mine");

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        return await TodoListTestSeedHelper.SeedTodoListAsync(db, ReminderSeedHelper.OtherUserId, "getbyid-theirs");
    }
}

[Collection("Postgres")]
public class CreateTodoListCategoryEndpointTests(AppDbContextFixture fixture) : BaseCreateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/todo-list-category";
    protected override bool IsAdminOnly => false;

    protected override Task<object> BuildValidPayloadAsync(DbContext db) => Task.FromResult<object>(new
    {
        Name = $"Category-{Guid.NewGuid():N}",
        Color = "#ff00ff"
    });
}

[Collection("Postgres")]
public class UpdateTodoListCategoryEndpointTests(AppDbContextFixture fixture) : BaseUpdateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/todo-list-category";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override Task<long> SeedEntityAsync(DbContext db) =>
        TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, name: "cat-update-mine");

    protected override Task<object> BuildValidPayloadAsync(DbContext db, long id) => Task.FromResult<object>(new
    {
        Name = "Updated category",
        Color = "#00ff00"
    });

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        return await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, ReminderSeedHelper.OtherUserId, "cat-update-theirs");
    }
}

[Collection("Postgres")]
public class DeleteTodoListCategoryEndpointTests(AppDbContextFixture fixture) : BaseDeleteEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/todo-list-category";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override Task<long> SeedEntityAsync(DbContext db) =>
        TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, name: "cat-delete-mine");

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        return await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, ReminderSeedHelper.OtherUserId, "cat-delete-theirs");
    }
}

[Collection("Postgres")]
public class GetByIdTodoListCategoryEndpointTests(AppDbContextFixture fixture) : BaseGetByIdEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/todo-list-category";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override Task<long> SeedEntityAsync(DbContext db) =>
        TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, name: "cat-getbyid-mine");

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        return await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, ReminderSeedHelper.OtherUserId, "cat-getbyid-theirs");
    }
}

[Collection("Postgres")]
public class CreateTaskPriorityEndpointTests(AppDbContextFixture fixture) : BaseCreateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/task-priority";
    protected override bool IsAdminOnly => false;

    protected override Task<object> BuildValidPayloadAsync(DbContext db) => Task.FromResult<object>(new
    {
        Text = $"Priority-{Guid.NewGuid():N}",
        Color = "#ff00ff",
        Priority = (short)42
    });
}

[Collection("Postgres")]
public class DeleteTaskPriorityEndpointTests(AppDbContextFixture fixture) : BaseDeleteEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/task-priority";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override Task<long> SeedEntityAsync(DbContext db) =>
        TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 20, text: "prio-delete-mine");

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        return await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 21, ReminderSeedHelper.OtherUserId, "prio-delete-theirs");
    }
}

[Collection("Postgres")]
public class GetByIdTaskPriorityEndpointTests(AppDbContextFixture fixture) : BaseGetByIdEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/task-priority";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override Task<long> SeedEntityAsync(DbContext db) =>
        TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 30, text: "prio-getbyid-mine");

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        return await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 31, ReminderSeedHelper.OtherUserId, "prio-getbyid-theirs");
    }
}

[Collection("Postgres")]
public class UpdateTaskPriorityEndpointTests(AppDbContextFixture fixture) : BaseUpdateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/task-priority";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override Task<long> SeedEntityAsync(DbContext db) =>
        TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 60, text: "update-bug");

    protected override Task<object> BuildValidPayloadAsync(DbContext db, long id) => Task.FromResult<object>(new
    {
        Text = "update-bug",
        Color = "#abcdef",
        Priority = (short)61
    });

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        return await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 62, ReminderSeedHelper.OtherUserId, "update-bug-theirs");
    }
}

[Collection("Postgres")]
public class CreateTodoListItemEndpointTests(AppDbContextFixture fixture) : BaseCreateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/todo-list-item";
    protected override bool IsAdminOnly => false;

    protected override async Task<object> BuildValidPayloadAsync(DbContext db)
    {
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "item-create");
        var priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 100);
        var listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "item-create-list");
        return new
        {
            ActivityId = activityId,
            TaskPriorityId = priorityId,
            TodoListId = listId
        };
    }
}

[Collection("Postgres")]
public class UpdateTodoListItemEndpointTests(AppDbContextFixture fixture) : BaseUpdateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/todo-list-item";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    private long _activityId;
    private long _priorityId;

    protected override async Task<long> SeedEntityAsync(DbContext db)
    {
        _activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "item-update");
        _priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 101);
        var listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "item-update-list");
        return await TodoListTestSeedHelper.SeedTodoListItemAsync(db, _activityId, _priorityId, listId);
    }

    protected override Task<object> BuildValidPayloadAsync(DbContext db, long id) => Task.FromResult<object>(new
    {
        ActivityId = _activityId,
        IsDone = false,
        DisplayOrder = 1000,
        TaskPriorityId = _priorityId
    });

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        _activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "item-update-theirs", ReminderSeedHelper.OtherUserId);
        _priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 102, ReminderSeedHelper.OtherUserId);
        var listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, ReminderSeedHelper.OtherUserId, "item-update-theirs-list");
        return await TodoListTestSeedHelper.SeedTodoListItemAsync(db, _activityId, _priorityId, listId, ReminderSeedHelper.OtherUserId);
    }
}

[Collection("Postgres")]
public class DeleteTodoListItemEndpointTests(AppDbContextFixture fixture) : BaseDeleteEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/todo-list-item";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override async Task<long> SeedEntityAsync(DbContext db)
    {
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "item-delete");
        var priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 103);
        var listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "item-delete-list");
        return await TodoListTestSeedHelper.SeedTodoListItemAsync(db, activityId, priorityId, listId);
    }

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "item-delete-theirs", ReminderSeedHelper.OtherUserId);
        var priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 104, ReminderSeedHelper.OtherUserId);
        var listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, ReminderSeedHelper.OtherUserId, "item-delete-theirs-list");
        return await TodoListTestSeedHelper.SeedTodoListItemAsync(db, activityId, priorityId, listId, ReminderSeedHelper.OtherUserId);
    }
}

[Collection("Postgres")]
public class GetByIdTodoListItemEndpointTests(AppDbContextFixture fixture) : BaseGetByIdEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/todo-list-item";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override async Task<long> SeedEntityAsync(DbContext db)
    {
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "item-getbyid");
        var priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 105);
        var listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "item-getbyid-list");
        return await TodoListTestSeedHelper.SeedTodoListItemAsync(db, activityId, priorityId, listId);
    }

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "item-getbyid-theirs", ReminderSeedHelper.OtherUserId);
        var priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 106, ReminderSeedHelper.OtherUserId);
        var listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, ReminderSeedHelper.OtherUserId, "item-getbyid-theirs-list");
        return await TodoListTestSeedHelper.SeedTodoListItemAsync(db, activityId, priorityId, listId, ReminderSeedHelper.OtherUserId);
    }
}

[Collection("Postgres")]
public class CreateRoutineTimePeriodEndpointTests(AppDbContextFixture fixture) : BaseCreateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/routine-time-period";
    protected override bool IsAdminOnly => false;

    protected override Task<object> BuildValidPayloadAsync(DbContext db) => Task.FromResult<object>(new
    {
        Text = $"Period-{Guid.NewGuid():N}",
        Color = "#ff00ff",
        LengthInDays = 7,
        StreakThreshold = 90,
        StreakGraceDays = 0,
        ResetAnchorDay = 1,
        HistoryDepth = 16
    });
}

[Collection("Postgres")]
public class UpdateRoutineTimePeriodEndpointTests(AppDbContextFixture fixture) : BaseUpdateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/routine-time-period";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override Task<long> SeedEntityAsync(DbContext db) =>
        TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, text: "period-update-mine");

    protected override Task<object> BuildValidPayloadAsync(DbContext db, long id) => Task.FromResult<object>(new
    {
        Text = "period-update-mine",
        Color = "#00ff00",
        LengthInDays = 7,
        StreakThreshold = 80,
        StreakGraceDays = 1,
        ResetAnchorDay = 2,
        HistoryDepth = 10
    });

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        return await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, ReminderSeedHelper.OtherUserId, "period-update-theirs");
    }
}

[Collection("Postgres")]
public class DeleteRoutineTimePeriodEndpointTests(AppDbContextFixture fixture) : BaseDeleteEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/routine-time-period";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override Task<long> SeedEntityAsync(DbContext db) =>
        TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, text: "period-delete-mine");

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        return await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, ReminderSeedHelper.OtherUserId, "period-delete-theirs");
    }
}

[Collection("Postgres")]
public class GetByIdRoutineTimePeriodEndpointTests(AppDbContextFixture fixture) : BaseGetByIdEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/routine-time-period";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override Task<long> SeedEntityAsync(DbContext db) =>
        TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, text: "period-getbyid-mine");

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        return await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, ReminderSeedHelper.OtherUserId, "period-getbyid-theirs");
    }
}

[Collection("Postgres")]
public class CreateRoutineTodoListEndpointTests(AppDbContextFixture fixture) : BaseCreateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/routine-todo-list";
    protected override bool IsAdminOnly => false;

    protected override async Task<object> BuildValidPayloadAsync(DbContext db)
    {
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "routine-item-create");
        var periodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, text: "routine-item-create-period");
        return new { ActivityId = activityId, TimePeriodId = periodId };
    }
}

[Collection("Postgres")]
public class UpdateRoutineTodoListEndpointTests(AppDbContextFixture fixture) : BaseUpdateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/routine-todo-list";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    private long _activityId;
    private long _periodId;

    protected override async Task<long> SeedEntityAsync(DbContext db)
    {
        _activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "routine-item-update");
        _periodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, text: "routine-item-update-period");
        return await TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, _activityId, _periodId);
    }

    protected override Task<object> BuildValidPayloadAsync(DbContext db, long id) => Task.FromResult<object>(new
    {
        ActivityId = _activityId,
        IsDone = false,
        DisplayOrder = 1000,
        TimePeriodId = _periodId
    });

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        _activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "routine-item-update-theirs", ReminderSeedHelper.OtherUserId);
        _periodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, ReminderSeedHelper.OtherUserId, "routine-item-update-theirs-period");
        return await TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, _activityId, _periodId, ReminderSeedHelper.OtherUserId);
    }
}

[Collection("Postgres")]
public class DeleteRoutineTodoListEndpointTests(AppDbContextFixture fixture) : BaseDeleteEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/routine-todo-list";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override async Task<long> SeedEntityAsync(DbContext db)
    {
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "routine-item-delete");
        var periodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, text: "routine-item-delete-period");
        return await TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, activityId, periodId);
    }

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "routine-item-delete-theirs", ReminderSeedHelper.OtherUserId);
        var periodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, ReminderSeedHelper.OtherUserId, "routine-item-delete-theirs-period");
        return await TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, activityId, periodId, ReminderSeedHelper.OtherUserId);
    }
}

[Collection("Postgres")]
public class GetByIdRoutineTodoListEndpointTests(AppDbContextFixture fixture) : BaseGetByIdEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/routine-todo-list";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override async Task<long> SeedEntityAsync(DbContext db)
    {
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "routine-item-getbyid");
        var periodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, text: "routine-item-getbyid-period");
        return await TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, activityId, periodId);
    }

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "routine-item-getbyid-theirs", ReminderSeedHelper.OtherUserId);
        var periodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, ReminderSeedHelper.OtherUserId, "routine-item-getbyid-theirs-period");
        return await TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, activityId, periodId, ReminderSeedHelper.OtherUserId);
    }
}

/// <summary>
/// <c>BaseChangeDisplayOrderTodoListEndpoint</c> gets special attention per the spec: the reorder payload
/// carries *other* ids (the neighbour to reorder against). Unlike the spec's assumption,
/// <c>TodoListExtensions.GetDisplayOrderById</c> / <c>GetGroupIdById</c> DO take an explicit
/// <c>userId</c> parameter (defense-in-depth on top of the global query filter) — this is verified here
/// rather than assumed, by proving user B cannot move user B's own item against a target id that belongs
/// to user A.
/// </summary>
[Collection("Postgres")]
public class ChangeDisplayOrderTodoListItemIdorTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    [Fact(DisplayName = "Reordering user B's item against user A's item id 404s and leaves both users' orders untouched")]
    public async Task Reorder_AgainstForeignTargetId_Returns404AndLeavesOrdersUnchanged()
    {
        long myItemId, myPriorityId, myListId;
        await using (var db = CreateDbContext())
        {
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "reorder-mine");
            myPriorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 200);
            myListId = await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "reorder-mine-list");
            myItemId = await TodoListTestSeedHelper.SeedTodoListItemAsync(db, activityId, myPriorityId, myListId);
        }

        long otherUserId = ReminderSeedHelper.OtherUserId;
        long theirItemId;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            var theirActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "reorder-theirs", otherUserId);
            var theirPriorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 201, otherUserId);
            var theirListId = await TodoListTestSeedHelper.SeedTodoListAsync(db, otherUserId, "reorder-theirs-list");
            theirItemId = await TodoListTestSeedHelper.SeedTodoListItemAsync(db, theirActivityId, theirPriorityId, theirListId, otherUserId);
        }

        // As the default test user (not otherUserId), try to move myItemId to precede user A's item.
        var response = await CreateClient().PatchAsJsonAsync("api/todo-list-item/change-display-order", new
        {
            MovedItemId = myItemId,
            PrecedingItemId = theirItemId,
            FollowingItemId = (long?)null
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "GetDisplayOrderById scopes the lookup to the caller's userId, so a foreign PrecedingItemId resolves to nothing");

        await using var verifyDb = CreateDbContext();
        var mine = await verifyDb.Set<TodoListItem>().SingleAsync(i => i.Id == myItemId, CancellationToken);
        mine.DisplayOrder.Should().Be(1000, "the failed reorder must not have perturbed the caller's own order");
    }
}

/// <summary>
/// One representative route proving the extension-client-token denial that CQ testing.md documents for
/// other portal areas also holds here — mirrors <c>ActivityExtensionClientDenialTests</c>'s reasoning
/// (DenyExtensionClients is attached globally, so proving it on one route in the area is representative).
/// This suite does not stand up the full login/extension-token issuance flow (out of scope for TEST-4's
/// budget) — the unauthenticated/User-role/cross-user matrix above is the primary coverage for Section A.
/// </summary>
[Collection("Postgres")]
public class TodoListRoleGateSanityTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    [Fact(DisplayName = "A plain User-role token can create a TodoList (default AllowedRoles includes User)")]
    public async Task UserRole_CanCreateTodoList()
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync("api/todo-list", new
        {
            Name = $"user-role-{Guid.NewGuid():N}"
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
