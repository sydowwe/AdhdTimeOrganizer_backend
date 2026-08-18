using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.timer;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.infrastructure.security;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.Testing;
using Sydowwe.Notifications.domain.entity;
using Sydowwe.Reminders.domain.entity;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-9 -- GDPR data export / account deletion. Both endpoints had zero test coverage before this file
/// per the prior review pass.
/// </summary>
file static class Seed
{
    public const string Password = "Test@1234!";

    public static async Task<long> SecondUserAsync(IPostgresFixture fixture, string email)
    {
        using var scope = fixture.UnauthenticatedFactory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = new User
        {
            Email = email,
            UserName = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            Locale = AvailableLocales.En,
            Timezone = TimeZoneInfo.Utc
        };
        var result = await userManager.CreateAsync(user, Password);
        result.Succeeded.Should().BeTrue();
        return user.Id;
    }

    public static async Task<long> TimerPresetAsync(DbContext db, long userId, int duration = 25)
    {
        var preset = new TimerPreset { UserId = userId, Duration = duration };
        db.Set<TimerPreset>().Add(preset);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return preset.Id;
    }

    public static async Task<long> TodoListAsync(DbContext db, long userId, string name)
    {
        var list = new TodoList { UserId = userId, Name = name };
        db.Set<TodoList>().Add(list);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return list.Id;
    }

    /// <summary>
    /// One row in every table the export claims to walk, plus the three the erasure has to reach without a
    /// cascade helping it. Shared by the export-completeness test and the deletion-cascade test so the two
    /// can never disagree about what "all of a user's data" means.
    /// </summary>
    public static async Task<SeededSliceData> EverySliceAsync(DbContext db, long userId, CancellationToken ct)
    {
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"Slice Activity {Guid.NewGuid():N}", userId, ct);
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, DateOnly.FromDateTime(DateTime.UtcNow), userId, ct);
        var plannerTaskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(
            db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), userId, ct: ct);
        var templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, userId, ct: ct);

        var todoListId = await TodoListTestSeedHelper.SeedTodoListAsync(db, userId, $"Slice List {Guid.NewGuid():N}", ct: ct);
        var priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 1, userId, ct: ct);
        var todoListItemId = await TodoListTestSeedHelper.SeedTodoListItemAsync(
            db, activityId, priorityId, todoListId, userId, ct: ct);

        var timePeriodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, userId, ct: ct);
        var routineTodoListId = await TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, activityId, timePeriodId, userId, ct: ct);

        var history = new ActivityHistory
        {
            UserId = userId,
            ActivityId = activityId,
            StartTimestamp = DateTime.UtcNow.AddHours(-1),
            EndTimestamp = DateTime.UtcNow.AddMinutes(-30),
            Length = new IntTime(0, 30)
        };
        db.Set<ActivityHistory>().Add(history);

        // RecordDate is derived from WindowStart; "now" keeps both entries inside
        // AppDbContext.CurrentPartitionDate, which the web-extension query filter enforces on reads.
        var windowStart = DateTime.UtcNow;
        var webEntry = new WebExtensionActivityEntry
        {
            UserId = userId,
            WindowStart = windowStart,
            Domain = "slice.example",
            Url = null,
            ActiveSeconds = 60,
            BackgroundSeconds = 0,
            IsFinal = true
        };
        db.Set<WebExtensionActivityEntry>().Add(webEntry);

        var desktopEntry = new DesktopActivityEntry
        {
            UserId = userId,
            WindowStart = windowStart,
            ProcessName = "slice.exe",
            ProductName = "Slice",
            WindowTitle = "t",
            ExecutablePath = "/usr/bin/slice",
            IsFullscreen = false,
            ActiveSeconds = 60,
            BackgroundSeconds = 0,
            IsPlayingSound = false,
            ActiveMonitor = 0
        };
        db.Set<DesktopActivityEntry>().Add(desktopEntry);

        // Quiet hours carries no FK to the user table on purpose (see NotificationQuietHours' XML doc), so
        // only NotificationSubjectDataEraser can ever remove it.
        var quietHours = new NotificationQuietHours { UserId = userId, StartMinute = 22 * 60, EndMinute = 6 * 60 };
        db.Set<NotificationQuietHours>().Add(quietHours);

        await db.SaveChangesAsync(ct);

        var timerPresetId = await TimerPresetAsync(db, userId);

        return new SeededSliceData(
            activityId, calendarId, plannerTaskId, templateId, todoListId, todoListItemId,
            routineTodoListId, history.Id, timerPresetId, quietHours.Id);
    }
}

file sealed record SeededSliceData(
    long ActivityId,
    long CalendarId,
    long PlannerTaskId,
    long TemplateId,
    long TodoListId,
    long TodoListItemId,
    long RoutineTodoListId,
    long ActivityHistoryId,
    long TimerPresetId,
    long QuietHoursId);

// =========================================================================================================
// GetUserDataExportEndpoint ("user/data-export")
// =========================================================================================================

[Collection("Postgres")]
public class UserDataExportTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    [Fact]
    public async Task Export_Unauthenticated_Returns401()
    {
        var response = await CreateUnauthenticatedClient().GetAsync("api/user/data-export", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Export_ReturnsOwnDataOnly_ExcludesOtherUsersRows()
    {
        // A dedicated user (rather than the fixture's shared TestUserId) so this test's call to the
        // 1/min export throttle never collides with another test's -- the throttle cache is a
        // process-lifetime singleton that PostgresTestBase.ResetAsync does not clear between tests.
        var callerId = await Seed.SecondUserAsync(Fixture, $"export-caller-{Guid.NewGuid():N}@test.com");

        await using var db = CreateDbContext();
        var ownListId = await Seed.TodoListAsync(db, callerId, "Own Export List");

        var otherUserId = await Seed.SecondUserAsync(Fixture, $"export-other-{Guid.NewGuid():N}@test.com");
        await Seed.TodoListAsync(db, otherUserId, "Foreign Export List");

        await using var factory = CreateFactory(TestRoles.AdminAndUser, callerId);
        var response = await factory.CreateClient().GetAsync("api/user/data-export", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken);

        var listIds = body.GetProperty("todoLists").EnumerateArray()
            .Select(l => l.GetProperty("id").GetInt64())
            .ToList();
        listIds.Should().Contain(ownListId, "the caller's own list must be in the export")
            .And.HaveCount(1, "another user's list must never leak into the export");
    }

    /// <summary>
    /// The positive half of the portability guarantee: every section the endpoint hand-picks must actually
    /// come back populated. Dropping one of the nine DbSet walks in
    /// <c>GetUserDataExportEndpoint.HandleAsync</c> compiles, still returns 200, and still passes a
    /// route-only test -- the caller just silently stops receiving a slice of their own data (Art. 15).
    /// </summary>
    [Fact]
    public async Task Export_IncludesEverySectionItWalks()
    {
        var callerId = await Seed.SecondUserAsync(Fixture, $"export-full-{Guid.NewGuid():N}@test.com");

        await using var db = CreateDbContext();
        var seeded = await Seed.EverySliceAsync(db, callerId, CancellationToken);

        await using var factory = CreateFactory(TestRoles.AdminAndUser, callerId);
        var response = await factory.CreateClient().GetAsync("api/user/data-export", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken);

        body.GetProperty("user").GetProperty("email").GetString()
            .Should().NotBeNullOrEmpty("the export identifies whose data it is");

        AssertContainsId(body, "plannerTasks", seeded.PlannerTaskId);
        AssertContainsId(body, "todoLists", seeded.TodoListId);
        AssertContainsId(body, "todoListItems", seeded.TodoListItemId);
        AssertContainsId(body, "routineTodoLists", seeded.RoutineTodoListId);
        AssertContainsId(body, "templates", seeded.TemplateId);
        AssertContainsId(body, "calendars", seeded.CalendarId);

        var tracking = body.GetProperty("activityTracking");
        AssertContainsId(tracking, "activityHistories", seeded.ActivityHistoryId);
        tracking.GetProperty("webTracking").GetArrayLength()
            .Should().BeGreaterThan(0, "the web-extension ledger is part of the export");
        tracking.GetProperty("desktopTracking").GetArrayLength()
            .Should().BeGreaterThan(0, "the desktop ledger is part of the export");
    }

    private static void AssertContainsId(JsonElement parent, string section, long expectedId) =>
        parent.GetProperty(section).EnumerateArray()
            .Select(e => e.GetProperty("id").GetInt64())
            .Should().Contain(expectedId, $"the export's '{section}' section must carry the caller's row");

    /// <summary>
    /// Portability means a file the user can keep, not just a 200 -- the attachment headers are the feature.
    /// </summary>
    [Fact]
    public async Task Export_IsServedAsAJsonAttachment()
    {
        var callerId = await Seed.SecondUserAsync(Fixture, $"export-headers-{Guid.NewGuid():N}@test.com");

        await using var factory = CreateFactory(TestRoles.AdminAndUser, callerId);
        var response = await factory.CreateClient().GetAsync("api/user/data-export", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var disposition = response.Content.Headers.ContentDisposition;
        disposition.Should().NotBeNull("the export is a download, not an inline body");
        disposition!.DispositionType.Should().Be("attachment");
        disposition.FileName.Should().Contain("export").And.EndWith(".json");
    }

    /// <summary>
    /// The endpoint advertises "max 1/min" and enforces it with a distributed-cache key. The throttle is the
    /// only thing standing between a full-database-read endpoint and a trivially repeatable one.
    /// </summary>
    [Fact]
    public async Task Export_SecondRequestWithinAMinute_Returns429()
    {
        var callerId = await Seed.SecondUserAsync(Fixture, $"export-throttle-{Guid.NewGuid():N}@test.com");

        await using var factory = CreateFactory(TestRoles.AdminAndUser, callerId);
        using var client = factory.CreateClient();

        (await client.GetAsync("api/user/data-export", CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.GetAsync("api/user/data-export", CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
                "the throttle key is set on the first call and lives for a minute");
    }

    /// <summary>
    /// GetUserDataExportEndpoint walks a hand-picked set of DbSets, not every user-scoped table, so the
    /// omissions have to be enumerated somewhere or they drift silently. This is that ledger: every
    /// <c>IEntityWithUser</c> in the model must be classified as exported or as a known gap, so <b>adding a
    /// new user-scoped entity fails this test</b> until someone decides which side it falls on. That
    /// decision is a GDPR Art. 15 one (does self-created user data have to be portable?), which is exactly
    /// the decision that otherwise gets made by nobody.
    /// <para>
    /// Keeping <see cref="KnownGaps"/> honest in the other direction matters too: a name that is no longer
    /// user-scoped, or is now exported, is stale documentation, so the test fails on that as well.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Export_EveryUserScopedEntityIsEitherExportedOrARecordedGap()
    {
        await using var db = CreateDbContext();

        var userScoped = db.Model.GetEntityTypes()
            .Where(t => typeof(IEntityWithUser).IsAssignableFrom(t.ClrType))
            .Select(t => t.ClrType.Name)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        var unclassified = userScoped.Except(Exported).Except(KnownGaps).ToList();
        unclassified.Should().BeEmpty(
            "a new user-scoped entity must be added to the export or recorded as a deliberate gap "
            + $"(unclassified: {string.Join(", ", unclassified)})");

        var stale = Exported.Concat(KnownGaps).Except(userScoped).ToList();
        stale.Should().BeEmpty(
            $"this ledger must not name entities that are no longer user-scoped (stale: {string.Join(", ", stale)})");

        Exported.Intersect(KnownGaps).Should().BeEmpty("an entity is either exported or it is not");
    }

    /// <summary>The nine entities <c>GetUserDataExportEndpoint</c> actually reads.</summary>
    private static readonly string[] Exported =
    [
        nameof(ActivityHistory),
        nameof(Calendar),
        nameof(DesktopActivityEntry),
        nameof(PlannerTask),
        nameof(RoutineTodoList),
        nameof(TaskPlannerDayTemplate),
        nameof(TodoList),
        nameof(TodoListItem),
        nameof(WebExtensionActivityEntry)
    ];

    /// <summary>
    /// User-scoped tables the export does not walk today, recorded rather than fixed silently -- deciding
    /// which of these belong in a portability export is a product/legal call, not a test's.
    /// <list type="bullet">
    /// <item>Session/infrastructure, legitimately out of scope: <c>RefreshToken</c>, <c>PushSubscription</c>,
    /// <c>NotificationPreference</c>.</item>
    /// <item>User-authored content, the ones that actually look like Art. 15 gaps: <c>TimerPreset</c>,
    /// <c>PomodoroTimerPreset</c>, <c>Reminder</c>, <c>RepeatingPlannerTask</c>, <c>TemplatePlannerTask</c>,
    /// <c>MemoryAnchor</c>, <c>AndroidSessionData</c>, the two tracker pattern-mapping tables and
    /// <c>LeisureSuggestionRecord</c>.</item>
    /// <item>Lookups and settings the exported rows refer to by id, so the export is readable but not
    /// self-contained: the <c>Activity*</c> family, <c>TodoListCategory</c>, <c>TaskPriority</c>,
    /// <c>TaskImportance</c>, <c>RoutineTimePeriod</c>, <c>UserPlannerSettings</c>,
    /// <c>PlannerSuggestionFromDayTemplate</c>.</item>
    /// </list>
    /// </summary>
    private static readonly string[] KnownGaps =
    [
        "Activity",
        "ActivityCategory",
        "ActivityExpectedCostTier",
        "ActivityExperienceType",
        "ActivityLocationType",
        "ActivityRole",
        "ActivityWeatherDependency",
        "AndroidSessionData",
        "LeisureSuggestionRecord",
        "MemoryAnchor",
        "NotificationPreference",
        "PlannerSuggestionFromDayTemplate",
        "PomodoroTimerPreset",
        "PushSubscription",
        "RefreshToken",
        "Reminder",
        "RepeatingPlannerTask",
        "RoutineTimePeriod",
        "TaskImportance",
        "TaskPriority",
        "TemplatePlannerTask",
        "TimerPreset",
        "TodoListCategory",
        "TrackerAndroidMappingByPattern",
        "TrackerDesktopMappingByPattern",
        "UserPlannerSettings"
    ];
}

// =========================================================================================================
// DeleteUserAccountEndpoint ("user/account", DELETE) -- gated by VerifyUserPreProcessor (password + 2FA).
// Uses AuthTestBase because deletion must be observed through the real login/refresh-token pipeline, not
// the RoleTestAuthHandler bypass every other test uses.
// =========================================================================================================

[Collection("Postgres")]
public class UserAccountDeletionTests(AppDbContextFixture fixture) : AuthTestBase(fixture)
{
    private const string Password = Seed.Password;

    private static async Task<(long UserId, string Email)> CreateRealUserAsync(
        IPostgresFixture fx, string emailPrefix, string password)
    {
        var email = $"{emailPrefix}-{Guid.NewGuid():N}@integration.com";
        await using var db = (AppDbContext)fx.CreateDbContext();
        var user = new User
        {
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            Locale = AvailableLocales.En,
            Timezone = TimeZoneInfo.Utc
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Without a role assignment the JWT authenticates but 403s on every endpoint (Program.cs's
        // global configurator defaults to Roles("User","Admin","Root")).
        var roleId = await db.Roles.Where(r => r.Name == PortalRoleCatalog.User).Select(r => r.Id).FirstAsync();
        db.UserRoles.Add(new IdentityUserRole<long> { UserId = user.Id, RoleId = roleId });
        await db.SaveChangesAsync();

        return (user.Id, email);
    }

    private static HttpRequestMessage DeleteRequest(string password, string? twoFactorToken = null) =>
        new(HttpMethod.Delete, "user/account")
        {
            Content = JsonContent.Create(new { Password = password, TwoFactorAuthToken = twoFactorToken })
        };

    [Fact]
    public async Task DeleteAccount_Unauthenticated_Returns401()
    {
        using var client = CreateRawClient();
        using var request = DeleteRequest("irrelevant");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteAccount_WrongPassword_Returns401AndAccountSurvives()
    {
        var (userId, email) = await CreateRealUserAsync(Fixture, "delete-wrongpw", Password);

        using var cookieClient = CreateCookieClient();
        await LoginAsync(cookieClient, email, Password);

        using var request = DeleteRequest("WrongPassword!1");
        var response = await cookieClient.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "an already-authenticated session must not be enough to delete the account -- the current password must be re-proven");

        await using var db = (AppDbContext)Fixture.CreateDbContext();
        (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == userId)).Should().BeTrue();
    }

    /// <summary>
    /// The second half of the step-up gate. <c>VerifyUserPreProcessor</c> checks password <i>and</i> TOTP, and
    /// the TOTP branch is a no-op for users without 2FA -- so only a 2FA-enabled account exercises it. 2FA is
    /// switched on directly in the DB after login rather than through the login flow: the pre-processor
    /// re-reads the user with <c>UserManager</c> on every call, which is the behaviour that matters here.
    /// </summary>
    [Fact]
    public async Task DeleteAccount_TwoFactorEnabled_WithoutToken_Returns401AndAccountSurvives()
    {
        var (userId, email) = await CreateRealUserAsync(Fixture, "delete-2fa", Password);

        using var cookieClient = CreateCookieClient();
        await LoginAsync(cookieClient, email, Password);

        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId);
            user.TwoFactorEnabled = true;
            await db.SaveChangesAsync();
        }

        using var noTokenRequest = DeleteRequest(Password);
        (await cookieClient.SendAsync(noTokenRequest)).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a correct password alone must not delete a 2FA-protected account");

        using var badTokenRequest = DeleteRequest(Password, "000000");
        (await cookieClient.SendAsync(badTokenRequest)).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a wrong TOTP must be rejected the same way a wrong password is");

        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == userId))
                .Should().BeTrue("neither rejected attempt may have deleted anything");
        }
    }

    [Fact]
    public async Task DeleteAccount_CorrectPassword_RemovesAccountAndCascadesSliceData()
    {
        var (userId, email) = await CreateRealUserAsync(Fixture, "delete-ok", Password);

        using var cookieClient = CreateCookieClient();
        await LoginAsync(cookieClient, email, Password);

        long todoListId;
        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            todoListId = await Seed.TodoListAsync(db, userId, "List To Be Erased");
        }

        using var request = DeleteRequest(Password);
        var response = await cookieClient.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == userId))
                .Should().BeFalse("the user row itself must be gone -- this is a hard delete, not a soft one");

            (await db.TodoLists.IgnoreQueryFilters().AnyAsync(l => l.Id == todoListId))
                .Should().BeFalse("IEntityWithUser rows cascade off the User FK (DeleteBehavior.Cascade) -- an orphaned todo list here would be a missed cascade");
        }
    }

    /// <summary>
    /// Erasure across every slice, not just the host's own tables. A missed cascade compiles fine, returns
    /// 204 and leaves a deleted person's planner, to-do, routine, history and tracking rows in the database
    /// indefinitely -- the exact silent failure this project's doctrine warns about, and a GDPR Art. 17
    /// breach rather than a bug. Quiet hours is in here on purpose: it carries no FK to the user table, so
    /// only <c>NotificationSubjectDataEraser</c> can remove it and no cascade covers for a broken fan-out.
    /// </summary>
    [Fact]
    public async Task DeleteAccount_ErasesUserDataAcrossEverySlice()
    {
        var (userId, email) = await CreateRealUserAsync(Fixture, "delete-slices", Password);

        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            await Seed.EverySliceAsync(db, userId, CancellationToken);
        }

        using var cookieClient = CreateCookieClient();
        await LoginAsync(cookieClient, email, Password);

        using var request = DeleteRequest(Password);
        (await cookieClient.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            await AssertNoRowsLeftAsync<Activity>(db, userId);
            await AssertNoRowsLeftAsync<ActivityRole>(db, userId);
            await AssertNoRowsLeftAsync<Calendar>(db, userId);
            await AssertNoRowsLeftAsync<PlannerTask>(db, userId);
            await AssertNoRowsLeftAsync<TaskPlannerDayTemplate>(db, userId);
            await AssertNoRowsLeftAsync<TodoList>(db, userId);
            await AssertNoRowsLeftAsync<TodoListItem>(db, userId);
            await AssertNoRowsLeftAsync<TaskPriority>(db, userId);
            await AssertNoRowsLeftAsync<RoutineTodoList>(db, userId);
            await AssertNoRowsLeftAsync<RoutineTimePeriod>(db, userId);
            await AssertNoRowsLeftAsync<ActivityHistory>(db, userId);
            await AssertNoRowsLeftAsync<WebExtensionActivityEntry>(db, userId);
            await AssertNoRowsLeftAsync<DesktopActivityEntry>(db, userId);
            await AssertNoRowsLeftAsync<TimerPreset>(db, userId);

            (await db.Set<NotificationQuietHours>().AnyAsync(q => q.UserId == userId, CancellationToken))
                .Should().BeFalse("quiet hours has no FK to the user -- only the NotificationSubjectDataEraser fan-out removes it");
        }
    }

    private static async Task AssertNoRowsLeftAsync<TEntity>(DbContext db, long userId)
        where TEntity : class, IEntityWithUser =>
        (await db.Set<TEntity>().IgnoreQueryFilters().AnyAsync(e => e.UserId == userId, CancellationToken))
            .Should().BeFalse($"{typeof(TEntity).Name} rows for a deleted account must not survive the erasure");

    /// <summary>
    /// Reminders keeps its recipient-side rows FK-free from the host on purpose (module boundary), so
    /// nothing about them is deleted by the DB cascade the portal-owned tables get. Erasure only happens
    /// because <c>DeleteUserAccountEndpoint.BeforeDeleteAsync</c> fans out over every registered
    /// <c>ISubjectDataEraser</c> -- if Reminders' eraser were ever dropped from DI (or the fan-out itself
    /// broken), this is exactly the kind of thing that would compile fine and silently leave orphaned
    /// rows scanning for a user who no longer exists, per this project's "Silent failures" doctrine.
    /// </summary>
    [Fact]
    public async Task DeleteAccount_ErasesReminderRecipientRows_ViaSubjectDataEraserFanOut()
    {
        var (userId, email) = await CreateRealUserAsync(Fixture, "delete-reminder", Password);

        using var cookieClient = CreateCookieClient();
        await LoginAsync(cookieClient, email, Password);

        var reminderPayload = new
        {
            Title = "Erasure fan-out reminder",
            RemindAt = DateTime.UtcNow.AddDays(1),
            LeadOffsetsMinutes = new[] { 0 }
        };
        (await cookieClient.PostAsJsonAsync("reminder", reminderPayload, JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            (await db.Set<ReminderRecipient>().AnyAsync(r => r.UserId == userId, CancellationToken))
                .Should().BeTrue("registering a reminder must create the caller's own recipient row");
        }

        using var request = DeleteRequest(Password);
        (await cookieClient.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            (await db.Set<ReminderRecipient>().AnyAsync(r => r.UserId == userId, CancellationToken))
                .Should().BeFalse("ReminderSubjectDataEraser must have removed the recipient row as part of account deletion");
        }
    }

    [Fact]
    public async Task DeleteAccount_RevokesRefreshToken()
    {
        var (userId, email) = await CreateRealUserAsync(Fixture, "delete-revoke", Password);

        using var cookieClient = CreateCookieClient();
        await LoginAsync(cookieClient, email, Password);

        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            (await db.RefreshTokens.IgnoreQueryFilters().AnyAsync(r => r.UserId == userId && !r.IsRevoked))
                .Should().BeTrue("login must create a live refresh token");
        }

        using var request = DeleteRequest(Password);
        (await cookieClient.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            // The row itself cascades away with the user, so "no live token remains" is what "revoked" means here.
            (await db.RefreshTokens.IgnoreQueryFilters().AnyAsync(r => r.UserId == userId && !r.IsRevoked))
                .Should().BeFalse("no refresh token for the deleted account may remain usable");
        }
    }

    /// <summary>
    /// The row-level check above proves the refresh token is gone from the database; this proves what the
    /// credentials the caller is still holding are actually worth. A raw client is required: a cookie
    /// container would obediently drop the cookies the delete response clears, which is precisely the
    /// client-side courtesy an attacker holding a stolen session would skip.
    /// <para>
    /// <b>Session revocation is only half-complete, and this test pins that.</b> The refresh path really is
    /// dead — the token row cascaded away with the user, so the session cannot be extended. The access token
    /// is a stateless ECDSA JWT and nothing revalidates it against the database: there is no
    /// <c>OnTokenValidated</c> hook and no <c>SecurityStampValidator</c> anywhere in this solution, so it
    /// keeps authenticating until it expires (<c>JwtService.AccessTokenExpiry</c>, 15 minutes). Reads return
    /// nothing, because the rows are gone; writes fail on the user FK. If a stamp/existence check is ever
    /// added, this assertion flips to 401 — and that failure is the intended signal, not a regression.
    /// </para>
    /// </summary>
    [Fact]
    public async Task DeleteAccount_ThenReplayingTheOldSession_KillsRefreshButNotTheLiveAccessToken()
    {
        var (_, email) = await CreateRealUserAsync(Fixture, "delete-replay", Password);

        using var client = CreateRawClient();
        var sessionCookies = await LoginCapturingCookiesAsync(client, email, Password);

        using var deleteRequest = DeleteRequest(Password);
        deleteRequest.Headers.Add("Cookie", sessionCookies);
        (await client.SendAsync(deleteRequest)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "auth/refresh");
        refreshRequest.Headers.Add("Cookie", sessionCookies);
        (await client.SendAsync(refreshRequest)).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the refresh token died with the account, so the session cannot be extended");

        using var probeRequest = new HttpRequestMessage(HttpMethod.Get, "timer-preset");
        probeRequest.Headers.Add("Cookie", sessionCookies);
        var probeResponse = await client.SendAsync(probeRequest);
        probeResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "documented residual: the stateless access token is not revalidated against the user table, so it "
            + "outlives the account until it expires -- see this test's remarks");

        (await probeResponse.Content.ReadAsStringAsync(CancellationToken))
            .Should().NotContain("\"id\"", "whatever the stale token still reaches, it must find no data");
    }

    /// <summary>
    /// Logs in on a cookie-container-free client and returns the auth + refresh cookies as a <c>Cookie</c>
    /// header, so a test can keep replaying them after the server has told the browser to drop them.
    /// </summary>
    private static async Task<string> LoginCapturingCookiesAsync(HttpClient rawClient, string email, string password)
    {
        var payload = new { Email = email, Password = password, StayLoggedIn = false, RecaptchaToken = "test", Timezone = "UTC" };
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/login");
        request.Headers.Add("X-Client-Id", Guid.NewGuid().ToString());
        request.Content = JsonContent.Create(payload);

        var response = await rawClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var cookies = response.Headers.GetValues("Set-Cookie")
            .Select(c => c.Split(';', 2)[0])
            .Where(c => c.StartsWith($"{AuthCookies.AuthTokenName}=", StringComparison.Ordinal)
                        || c.StartsWith($"{AuthCookies.RefreshTokenName}=", StringComparison.Ordinal))
            .ToList();

        cookies.Should().HaveCount(2, "login must issue both the access and the refresh cookie");
        return string.Join("; ", cookies);
    }

    [Fact]
    public async Task DeleteAccount_ThenReRegisterWithSameEmail_Succeeds()
    {
        var (_, email) = await CreateRealUserAsync(Fixture, "delete-reregister", Password);

        using var cookieClient = CreateCookieClient();
        await LoginAsync(cookieClient, email, Password);

        using var deleteRequest = DeleteRequest(Password);
        (await cookieClient.SendAsync(deleteRequest)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var registerClient = CreateCookieClient();
        var registerPayload = new
        {
            Email = email,
            Password = "BrandNewPassword1!",
            RecaptchaToken = "test",
            TwoFactorEnabled = false,
            CurrentLocale = (int)AvailableLocales.En,
            Timezone = "UTC"
        };
        var response = await registerClient.PostAsJsonAsync("auth/register", registerPayload, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "deletion is a hard delete, so the email must be free to register again -- a 409 here would mean the old row survived");
    }

    [Fact]
    public async Task DeleteAccount_OnlyDeletesCallersOwnAccount_OtherUserUnaffected()
    {
        var (callerId, callerEmail) = await CreateRealUserAsync(Fixture, "delete-caller", Password);
        var (otherUserId, _) = await CreateRealUserAsync(Fixture, "delete-bystander", Password);

        using var cookieClient = CreateCookieClient();
        await LoginAsync(cookieClient, callerEmail, Password);

        using var request = DeleteRequest(Password);
        (await cookieClient.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var db = (AppDbContext)Fixture.CreateDbContext();
        (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == callerId)).Should().BeFalse();
        (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == otherUserId))
            .Should().BeTrue("deletion must never reach an account other than the caller's own -- the endpoint takes no id, only the verified session");
    }
}
