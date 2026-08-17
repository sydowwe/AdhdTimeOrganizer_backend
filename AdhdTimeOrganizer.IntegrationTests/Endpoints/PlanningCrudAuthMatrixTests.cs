using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing.baseTests;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-3 / Section A — the plain CRUD auth matrix (401 unauthenticated, 404 IDOR, User role allowed) for
/// every <c>{id}</c> route across the seven activity-planning endpoint subfolders, driven off the shared
/// framework endpoint test bases (<c>Sydowwe.Framework.Testing.baseTests</c>).
/// <para>
/// This is the first time a portal endpoint has subclassed those bases. The friction: every entity here is
/// <c>IEntityWithUser</c> and covered by <c>AppDbContext</c>'s global query filter, so a cross-user id is
/// filtered out of the lookup before any <c>AuthorizeAsync</c> hook runs — the answer is 404, not the bases'
/// default 403 (same reasoning as <c>ReminderEndpointTests</c>). <see cref="BaseUpdateEndpointTests"/>,
/// <see cref="BaseDeleteEndpointTests"/> and <see cref="BaseGetByIdEndpointTests"/> all expose a virtual
/// <c>UnauthorizedStatus</c> for exactly this override — <see cref="BaseBatchDeleteEndpointTests"/> does not,
/// so its cross-user case is asserted separately below instead of through the base's <c>NotOwner_Returns403</c>.
/// </para>
/// <para>
/// None of these endpoints override <c>AllowedRoles()</c>, so the default User+Admin+Root applies everywhere
/// — <c>IsAdminOnly = false</c> throughout, and <see cref="PlanningUserRoleAllowedTests"/> below asserts the
/// positive "a plain User role reaches it" half the bases don't cover on their own.
/// </para>
/// </summary>
[Collection("Postgres")]
public class GetByIdPlannerTaskEndpointTests(AppDbContextFixture fixture) : BaseGetByIdEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/planner-task";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override async Task<long> SeedEntityAsync(DbContext db)
    {
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "getbyid-mine");
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 1, 10));
        return await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0));
    }

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "getbyid-theirs", ReminderSeedHelper.OtherUserId);
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 1, 11), ReminderSeedHelper.OtherUserId);
        return await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), ReminderSeedHelper.OtherUserId);
    }
}

[Collection("Postgres")]
public class UpdatePlannerTaskEndpointTests(AppDbContextFixture fixture) : BaseUpdateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/planner-task";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    private long _activityId;

    protected override async Task<long> SeedEntityAsync(DbContext db)
    {
        _activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "update-mine");
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 1, 12));
        return await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, _activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0));
    }

    protected override Task<object> BuildValidPayloadAsync(DbContext db, long id) => Task.FromResult<object>(new
    {
        StartTime = new { Hours = 9, Minutes = 30 },
        EndTime = new { Hours = 10, Minutes = 30 },
        IsBackground = false,
        ActivityId = _activityId,
        Status = "InProgress",
        CalendarId = (long?)null,
        Date = new DateOnly(2027, 1, 12)
    });

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        _activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "update-theirs", ReminderSeedHelper.OtherUserId);
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 1, 13), ReminderSeedHelper.OtherUserId);
        return await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, _activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), ReminderSeedHelper.OtherUserId);
    }
}

[Collection("Postgres")]
public class DeletePlannerTaskEndpointTests(AppDbContextFixture fixture) : BaseDeleteEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/planner-task";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override async Task<long> SeedEntityAsync(DbContext db)
    {
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "delete-mine");
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 1, 14));
        return await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0));
    }

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "delete-theirs", ReminderSeedHelper.OtherUserId);
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 1, 15), ReminderSeedHelper.OtherUserId);
        return await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), ReminderSeedHelper.OtherUserId);
    }
}

[Collection("Postgres")]
public class PatchPlannerTaskSpanEndpointTests(AppDbContextFixture fixture) : BasePatchEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/planner-task";
    protected override bool IsAdminOnly => false;

    protected override async Task<long> SeedEntityAsync(DbContext db)
    {
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "span-mine");
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 1, 16));
        return await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0));
    }

    protected override Task<object> BuildValidPayloadAsync(DbContext db, long id) => Task.FromResult<object>(new
    {
        StartTime = new { Hours = 11, Minutes = 0 },
        EndTime = new { Hours = 12, Minutes = 0 }
    });

    // BasePatchEndpoint has no ownership hook of its own for PlannerTask (the underlying entity lookup is
    // still user-filtered), but the endpoint doesn't override AuthorizeAsync, so the base's own 403 default
    // never fires for this entity; the IDOR path is covered by GetByIdPlannerTaskEndpointTests instead.
}

[Collection("Postgres")]
public class GetByIdCalendarEndpointTests(AppDbContextFixture fixture) : BaseGetByIdEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/calendar";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override Task<long> SeedEntityAsync(DbContext db) =>
        PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 2, 1));

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        return await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 2, 2), ReminderSeedHelper.OtherUserId);
    }
}

[Collection("Postgres")]
public class UpdateCalendarEndpointTests(AppDbContextFixture fixture) : BaseUpdateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/calendar";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override Task<long> SeedEntityAsync(DbContext db) =>
        // A Wednesday, so DayType.Workday validates.
        PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 2, 3));

    protected override Task<object> BuildValidPayloadAsync(DbContext db, long id) => Task.FromResult<object>(new
    {
        DayType = "Workday",
        WakeUpTime = new { Hours = 6, Minutes = 30 },
        BedTime = new { Hours = 22, Minutes = 0 }
    });

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        return await PlanningTestSeedHelper.SeedCalendarAsync(db, new DateOnly(2027, 2, 4), ReminderSeedHelper.OtherUserId);
    }
}

[Collection("Postgres")]
public class CreateTaskImportanceEndpointTests(AppDbContextFixture fixture) : BaseCreateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/task-importance";
    protected override bool IsAdminOnly => false;

    protected override Task<object> BuildValidPayloadAsync(DbContext db) => Task.FromResult<object>(new
    {
        Text = $"Create-{Guid.NewGuid():N}",
        Color = "#ff00ff",
        Importance = 42
    });
}

[Collection("Postgres")]
public class UpdateTaskImportanceEndpointTests(AppDbContextFixture fixture) : BaseUpdateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/task-importance";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override Task<long> SeedEntityAsync(DbContext db) =>
        PlanningTestSeedHelper.SeedTaskImportanceAsync(db, 10);

    protected override Task<object> BuildValidPayloadAsync(DbContext db, long id) => Task.FromResult<object>(new
    {
        Text = "Updated",
        Color = "#00ff00",
        Importance = 11
    });

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        return await PlanningTestSeedHelper.SeedTaskImportanceAsync(db, 12, ReminderSeedHelper.OtherUserId);
    }
}

[Collection("Postgres")]
public class DeleteTaskImportanceEndpointTests(AppDbContextFixture fixture) : BaseDeleteEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/task-importance";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override Task<long> SeedEntityAsync(DbContext db) =>
        PlanningTestSeedHelper.SeedTaskImportanceAsync(db, 20);

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        return await PlanningTestSeedHelper.SeedTaskImportanceAsync(db, 21, ReminderSeedHelper.OtherUserId);
    }
}

[Collection("Postgres")]
public class GetByIdTaskImportanceEndpointTests(AppDbContextFixture fixture) : BaseGetByIdEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/task-importance";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override Task<long> SeedEntityAsync(DbContext db) =>
        PlanningTestSeedHelper.SeedTaskImportanceAsync(db, 30);

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        return await PlanningTestSeedHelper.SeedTaskImportanceAsync(db, 31, ReminderSeedHelper.OtherUserId);
    }
}

[Collection("Postgres")]
public class CreateRepeatingPlannerTaskEndpointTests(AppDbContextFixture fixture) : BaseCreateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/repeating-planner-task";
    protected override bool IsAdminOnly => false;

    protected override async Task<object> BuildValidPayloadAsync(DbContext db)
    {
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "repeating-create");
        var importanceId = await PlanningTestSeedHelper.SeedTaskImportanceAsync(db, 50);
        return new
        {
            StartTime = new { Hours = 9, Minutes = 0 },
            EndTime = new { Hours = 10, Minutes = 0 },
            IsBackground = false,
            ActivityId = activityId,
            // RepeatingPlannerTaskValidator requires ImportanceId (NotNull), unlike PlannerTaskValidator where
            // it's optional -- a genuine asymmetry between the two, not a copy-paste typo to work around silently.
            ImportanceId = importanceId,
            IsActive = true,
            RecurrenceType = "DayOfWeek",
            ScheduledDays = new[] { "Monday" }
        };
    }
}

[Collection("Postgres")]
public class UpdateRepeatingPlannerTaskEndpointTests(AppDbContextFixture fixture) : BaseUpdateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/repeating-planner-task";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    private long _activityId;
    private long _importanceId;

    protected override async Task<long> SeedEntityAsync(DbContext db)
    {
        _activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "repeating-update");
        _importanceId = await PlanningTestSeedHelper.SeedTaskImportanceAsync(db, 51);
        return await PlanningTestSeedHelper.SeedRepeatingPlannerTaskAsync(db, _activityId);
    }

    protected override Task<object> BuildValidPayloadAsync(DbContext db, long id) => Task.FromResult<object>(new
    {
        StartTime = new { Hours = 8, Minutes = 0 },
        EndTime = new { Hours = 9, Minutes = 0 },
        IsBackground = false,
        ActivityId = _activityId,
        ImportanceId = _importanceId,
        IsActive = true,
        RecurrenceType = "DayOfWeek",
        ScheduledDays = new[] { "Tuesday" }
    });

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        _activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "repeating-theirs", ReminderSeedHelper.OtherUserId);
        _importanceId = await PlanningTestSeedHelper.SeedTaskImportanceAsync(db, 52, ReminderSeedHelper.OtherUserId);
        return await PlanningTestSeedHelper.SeedRepeatingPlannerTaskAsync(db, _activityId, ReminderSeedHelper.OtherUserId);
    }
}

[Collection("Postgres")]
public class DeleteRepeatingPlannerTaskEndpointTests(AppDbContextFixture fixture) : BaseDeleteEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/repeating-planner-task";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override async Task<long> SeedEntityAsync(DbContext db)
    {
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "repeating-delete");
        return await PlanningTestSeedHelper.SeedRepeatingPlannerTaskAsync(db, activityId);
    }

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "repeating-delete-theirs", ReminderSeedHelper.OtherUserId);
        return await PlanningTestSeedHelper.SeedRepeatingPlannerTaskAsync(db, activityId, ReminderSeedHelper.OtherUserId);
    }
}

[Collection("Postgres")]
public class GetByIdRepeatingPlannerTaskEndpointTests(AppDbContextFixture fixture) : BaseGetByIdEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/repeating-planner-task";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override async Task<long> SeedEntityAsync(DbContext db)
    {
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "repeating-getbyid");
        return await PlanningTestSeedHelper.SeedRepeatingPlannerTaskAsync(db, activityId);
    }

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "repeating-getbyid-theirs", ReminderSeedHelper.OtherUserId);
        return await PlanningTestSeedHelper.SeedRepeatingPlannerTaskAsync(db, activityId, ReminderSeedHelper.OtherUserId);
    }
}

[Collection("Postgres")]
public class CreateTaskPlannerDayTemplateEndpointTests(AppDbContextFixture fixture) : BaseCreateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/task-planner-day-template";
    protected override bool IsAdminOnly => false;

    protected override Task<object> BuildValidPayloadAsync(DbContext db) => Task.FromResult<object>(new
    {
        Name = $"Template-{Guid.NewGuid():N}",
        IsActive = true,
        SuggestedForDayType = "Workday"
    });
}

[Collection("Postgres")]
public class UpdateTaskPlannerDayTemplateEndpointTests(AppDbContextFixture fixture) : BaseUpdateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/task-planner-day-template";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override Task<long> SeedEntityAsync(DbContext db) =>
        PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db);

    protected override Task<object> BuildValidPayloadAsync(DbContext db, long id) => Task.FromResult<object>(new
    {
        Name = "Updated template",
        IsActive = false,
        SuggestedForDayType = "Weekend"
    });

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        return await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, ReminderSeedHelper.OtherUserId);
    }
}

[Collection("Postgres")]
public class DeleteTaskPlannerDayTemplateEndpointTests(AppDbContextFixture fixture) : BaseDeleteEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/task-planner-day-template";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override Task<long> SeedEntityAsync(DbContext db) =>
        PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db);

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        return await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, ReminderSeedHelper.OtherUserId);
    }
}

[Collection("Postgres")]
public class GetByIdTaskPlannerDayTemplateEndpointTests(AppDbContextFixture fixture) : BaseGetByIdEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/task-planner-day-template";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override Task<long> SeedEntityAsync(DbContext db) =>
        PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db);

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        return await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, ReminderSeedHelper.OtherUserId);
    }
}

[Collection("Postgres")]
public class CreateTemplatePlannerTaskEndpointTests(AppDbContextFixture fixture) : BaseCreateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/template-planner-task";
    protected override bool IsAdminOnly => false;

    protected override async Task<object> BuildValidPayloadAsync(DbContext db)
    {
        var templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db);
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "template-task-create");
        return new
        {
            TemplateId = templateId,
            ActivityId = activityId,
            StartTime = new { Hours = 9, Minutes = 0 },
            EndTime = new { Hours = 10, Minutes = 0 },
            IsBackground = false
        };
    }
}

[Collection("Postgres")]
public class UpdateTemplatePlannerTaskEndpointTests(AppDbContextFixture fixture) : BaseUpdateEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/template-planner-task";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    private long _templateId;
    private long _activityId;

    protected override async Task<long> SeedEntityAsync(DbContext db)
    {
        _templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db);
        _activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "template-task-update");
        return await PlanningTestSeedHelper.SeedTemplatePlannerTaskAsync(db, _templateId, _activityId, new TimeOnly(9, 0), new TimeOnly(10, 0));
    }

    protected override Task<object> BuildValidPayloadAsync(DbContext db, long id) => Task.FromResult<object>(new
    {
        TemplateId = _templateId,
        ActivityId = _activityId,
        StartTime = new { Hours = 11, Minutes = 0 },
        EndTime = new { Hours = 12, Minutes = 0 },
        IsBackground = false
    });

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        _templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, ReminderSeedHelper.OtherUserId);
        _activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "template-task-theirs", ReminderSeedHelper.OtherUserId);
        return await PlanningTestSeedHelper.SeedTemplatePlannerTaskAsync(db, _templateId, _activityId, new TimeOnly(9, 0), new TimeOnly(10, 0), ReminderSeedHelper.OtherUserId);
    }
}

[Collection("Postgres")]
public class DeleteTemplatePlannerTaskEndpointTests(AppDbContextFixture fixture) : BaseDeleteEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/template-planner-task";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override async Task<long> SeedEntityAsync(DbContext db)
    {
        var templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db);
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "template-task-delete");
        return await PlanningTestSeedHelper.SeedTemplatePlannerTaskAsync(db, templateId, activityId, new TimeOnly(9, 0), new TimeOnly(10, 0));
    }

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        var templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, ReminderSeedHelper.OtherUserId);
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "template-task-delete-theirs", ReminderSeedHelper.OtherUserId);
        return await PlanningTestSeedHelper.SeedTemplatePlannerTaskAsync(db, templateId, activityId, new TimeOnly(9, 0), new TimeOnly(10, 0), ReminderSeedHelper.OtherUserId);
    }
}

/// <summary>
/// Was a 🔴 FINDING: <c>TemplatePlannerTaskResponse.Projection</c> used to set <c>Color = entity.Color</c>,
/// where <c>TemplatePlannerTask.Color</c> is the C#-only computed property <c>Activity.Role.Color</c> — EF
/// Core cannot translate a call to that property inside a <c>.Select()</c> that becomes SQL (no implicit
/// client evaluation since EF Core 3), so every call to this endpoint threw and was caught by the base's
/// generic <c>catch (Exception)</c> as a 500. Fixed by inlining <c>Color = entity.Activity.Role.Color</c>
/// (matching how <c>RepeatingPlannerTaskResponse.Projection</c> already did it). <c>FilterTemplatePlannerTaskEndpoint</c>
/// shared the same projection and was fixed by the same change.
/// </summary>
[Collection("Postgres")]
public class GetByIdTemplatePlannerTaskEndpointTests(AppDbContextFixture fixture) : BaseGetByIdEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/template-planner-task";
    protected override bool IsAdminOnly => false;
    protected override HttpStatusCode UnauthorizedStatus => HttpStatusCode.NotFound;

    protected override async Task<long> SeedEntityAsync(DbContext db)
    {
        var templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db);
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "template-task-getbyid");
        return await PlanningTestSeedHelper.SeedTemplatePlannerTaskAsync(db, templateId, activityId, new TimeOnly(9, 0), new TimeOnly(10, 0));
    }

    protected override async Task<long?> SeedEntityOwnedByOtherUserAsync(DbContext db)
    {
        await ReminderSeedHelper.EnsureOtherUserAsync(db, TestContext.Current.CancellationToken);
        var templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, ReminderSeedHelper.OtherUserId);
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "template-task-getbyid-theirs", ReminderSeedHelper.OtherUserId);
        return await PlanningTestSeedHelper.SeedTemplatePlannerTaskAsync(db, templateId, activityId, new TimeOnly(9, 0), new TimeOnly(10, 0), ReminderSeedHelper.OtherUserId);
    }
}

[Collection("Postgres")]
public class BatchDeleteTemplatePlannerTaskEndpointTests(AppDbContextFixture fixture) : BaseBatchDeleteEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/template-planner-task/batch-delete";
    protected override bool IsAdminOnly => false;

    // Deliberately no SeedEntityOwnedByOtherUserAsync override: the base's own NotOwner_Returns403 hardcodes
    // 403, but TemplatePlannerTask is IEntityWithUser and covered by the global query filter, so a foreign id
    // is filtered out of the batch's own lookup and answers 404 (matching the count-mismatch branch), not 403.
    // See BatchDeleteTemplatePlannerTask_CrossUserId_Returns404NotForbidden below for the real assertion.

    protected override async Task<long[]> SeedEntitiesAsync(DbContext db, int count)
    {
        var templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db);
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "template-task-batchdelete");
        var ids = new long[count];
        for (var i = 0; i < count; i++)
            ids[i] = await PlanningTestSeedHelper.SeedTemplatePlannerTaskAsync(
                db, templateId, activityId, new TimeOnly(9 + i, 0), new TimeOnly(10 + i, 0));
        return ids;
    }

    [Fact(DisplayName = "Batch-deleting another user's template task 404s (count-mismatch path), not 403")]
    public async Task BatchDeleteTemplatePlannerTask_CrossUserId_Returns404NotForbidden()
    {
        await using var db = CreateDbContext();
        await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
        var templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, ReminderSeedHelper.OtherUserId);
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "template-task-batchdelete-theirs", ReminderSeedHelper.OtherUserId);
        var theirId = await PlanningTestSeedHelper.SeedTemplatePlannerTaskAsync(
            db, templateId, activityId, new TimeOnly(9, 0), new TimeOnly(10, 0), ReminderSeedHelper.OtherUserId);

        var response = await CreateClient().PostAsJsonAsync(EndpointUrl, new { ids = new[] { theirId } }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var survivor = await db.Set<TemplatePlannerTask>().IgnoreQueryFilters().SingleOrDefaultAsync(t => t.Id == theirId, CancellationToken);
        survivor.Should().NotBeNull("a 404 on the batch means nothing in it was touched");
    }
}
