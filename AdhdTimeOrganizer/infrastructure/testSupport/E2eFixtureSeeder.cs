using AdhdTimeOrganizer.Planning.application.notification;
using AdhdTimeOrganizer.Routines.application.notification;
using AdhdTimeOrganizer.History.domain.model.@enum;
using AdhdTimeOrganizer.History.application.notification;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.application.dto.testSupport;
using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.infrastructure.security;
using AdhdTimeOrganizer.History.application.job;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.Planning.application.service.reminder;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.entity.reminder;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.android;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Contracts.reminders;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.domain.serviceContract;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface.manager;
using Sydowwe.Notifications.domain.entity;
using Sydowwe.Reminders.domain.entity;
using Sydowwe.Reminders.domain.@enum;
using Sydowwe.Scheduler.domain.entity;
using Sydowwe.Scheduler.domain.@enum;
using DayTypeEnum = AdhdTimeOrganizer.Core.domain.model.@enum.DayType;
using LocationEnum = AdhdTimeOrganizer.Core.domain.model.@enum.Location;
using static AdhdTimeOrganizer.infrastructure.testSupport.E2eFixtureIds;

namespace AdhdTimeOrganizer.infrastructure.testSupport;

public interface IE2eFixtureSeeder
{
    Task<TestSeedResponse> ResetAsync(CancellationToken ct);
}

/// <summary>
/// Puts the database into the one state the Playwright suite expects, from whatever state it was in.
///
/// <para><b>Reset, not upsert.</b> It truncates every table in the model with <c>RESTART IDENTITY
/// CASCADE</c> and rebuilds — including the root admin, through the ordinary app-wide default seeders.
/// Upserting instead would leave rows a previous test run created, which is the polluted state this
/// endpoint exists to make impossible. The <c>scheduler</c> schema is the one exclusion: the job
/// registry and its run ledger are operational rather than fixture data, and the registrars that
/// repopulate them only run at boot.</para>
///
/// <para><b>This is a fixture, not a dev seeder.</b> It deliberately does not implement
/// <c>IPerUserDevSeeder</c>: those run in Order bands as one chain over the root admin, log-and-continue
/// on failure, and produce a lived-in dataset whose ids are incidental. A spec needs the opposite —
/// two specific accounts, known ids, and a hard failure if any part of it did not land. There is
/// therefore no entry in <c>SeederOrderBands.md</c> for it.</para>
///
/// <para><b>What it does reuse:</b> <see cref="IAppWideDefaultSeederManager"/> for the roles and the
/// root admin, and <see cref="IUserDefaultsService"/> for each account's per-user defaults (activity
/// roles, the four leisure lookups, timer presets, task priorities, task importances, routine periods,
/// planner settings and two years of calendar days). Those are the real sign-up path, so a fixture
/// account is shaped exactly like an account the app created.</para>
/// </summary>
public class E2eFixtureSeeder(
    AppDbContext dbContext,
    IAppWideDefaultSeederManager appWideDefaults,
    IUserDefaultsService userDefaults,
    IPasswordHasher<User> passwordHasher,
    IActivityTimeAttributionSink attributionSink,
    ILogger<E2eFixtureSeeder> logger) : IScopedService, IE2eFixtureSeeder
{
    /// <summary>
    /// The estimate-vs-actual set. Each entry becomes one activity, one to-do item carrying
    /// <see cref="BaseTodoListItem.SuggestedTime"/>, and three <see cref="ActivityHistory"/> rows on
    /// that same activity — the shape both the per-item "usually Nm" chip and the header ratio line
    /// read. The actual minutes deliberately straddle the estimate rather than matching it: a set that
    /// agreed with itself would render a ratio of exactly 1.00 and prove nothing.
    /// </summary>
    private static readonly CalibrationEntry[] CalibrationSet =
    [
        new(ActivityBugFixing, ItemBugFixing, "Bug Fixing", PrimaryRoleWork, PrimaryCategoryDeepWork,
            EstimateMinutes: 45, ActualMinutes: [62, 51, 70]),
        new(ActivityFeatureDevelopment, ItemFeatureDevelopment, "Feature Development", PrimaryRoleWork, PrimaryCategoryDeepWork,
            EstimateMinutes: 120, ActualMinutes: [145, 160, 135]),
        new(ActivityCodeReview, ItemCodeReview, "Code Review", PrimaryRoleWork, PrimaryCategoryQuickTasks,
            EstimateMinutes: 30, ActualMinutes: [25, 34, 28]),
        new(ActivityMorningExercise, ItemMorningExercise, "Morning Exercise", PrimaryRoleHealth, null,
            EstimateMinutes: 40, ActualMinutes: [38, 42, 40]),
        new(ActivityGroceryShopping, ItemGroceryShopping, "Grocery Shopping", PrimaryRoleHealth, PrimaryCategoryErrands,
            EstimateMinutes: 60, ActualMinutes: [75, 80, 68]),
        new(ActivityMealPreparation, ItemMealPreparation, "Meal Preparation", PrimaryRoleHealth, PrimaryCategoryErrands,
            EstimateMinutes: 75, ActualMinutes: [90, 85, 95])
    ];

    /// <summary>
    /// The overdue set: activity, item, and how many days past due. Six of them, spread from one day to
    /// a fortnight. The renegotiation banner's threshold is a SPA constant this project cannot see, so
    /// the count aims well above any plausible value rather than at a specific one.
    /// </summary>
    private static readonly OverdueEntry[] OverdueSet =
    [
        new(ActivityRenewPassport, ItemRenewPassport, "Renew Passport", PrimaryRoleHealth, DaysOverdue: 14),
        new(ActivityFileTaxReturn, ItemFileTaxReturn, "File Tax Return", PrimaryRoleWork, DaysOverdue: 10),
        new(ActivityCallTheDentist, ItemCallTheDentist, "Call the Dentist", PrimaryRoleHealth, DaysOverdue: 7),
        new(ActivityFixTheBike, ItemFixTheBike, "Fix the Bike", PrimaryRoleLeisure, DaysOverdue: 5),
        new(ActivityReplyToLandlord, ItemReplyToLandlord, "Reply to Landlord", PrimaryRoleWork, DaysOverdue: 3),
        new(ActivityReturnLibraryBook, ItemReturnLibraryBook, "Return Library Book", PrimaryRoleHealth, DaysOverdue: 1)
    ];

    /// <summary>
    /// How many days of tracking the three ledgers carry, counting back from the seed date inclusive. Seven,
    /// because the dashboards' widest default range is a week and a range that outruns the data renders as
    /// the empty state this fixture exists to remove.
    /// </summary>
    private const int TrackingDays = 7;

    /// <summary>
    /// The browsing ledger, as contiguous one-minute windows. Each block writes one row per minute for its
    /// primary domain and one for the background domain, which is the shape
    /// <c>WebExtensionDataHeartbeatEndpoint</c> stores and <c>WebExtensionTimelineEndpoint</c> stitches back
    /// into sessions — it derives each window's end as <c>WindowStart.AddMinutes(1)</c> and joins adjacent
    /// windows, so a gap in the minutes is a gap in the timeline.
    /// </summary>
    private static readonly WebBlock[] WebBlocks =
    [
        new(StartHour: 9, Minutes: 30, "github.com", "https://github.com/sydowwe/adhd-time-organizer/pulls", "docs.microsoft.com"),
        new(StartHour: 13, Minutes: 25, "chatgpt.com", "https://chatgpt.com/", "github.com"),
        new(StartHour: 19, Minutes: 20, "youtube.com", "https://youtube.com/watch", "reddit.com")
    ];

    /// <summary>
    /// The desktop ledger. <c>rider64.exe</c> is the one block recorded at a full 60 active seconds per
    /// window — that is what makes the windows contiguous in the ledger's own terms, so the attribution the
    /// heartbeat performs collapses into one <c>ActivityHistory</c> row per block instead of one per minute
    /// (<c>ActivityHistoryTimeAttributionSink</c> extends a record whose end equals the next window's start).
    /// <para>
    /// The two window titles per process are what the distinct-entries grid groups on, together with the
    /// process and product names — one title would make that grid one row per process.
    /// </para>
    /// </summary>
    private static readonly DesktopBlock[] DesktopBlocks =
    [
        new(StartHour: 9, Minutes: 30, "rider64.exe", "JetBrains Rider", @"C:\Program Files\JetBrains\Rider\bin\rider64.exe",
            ["AdhdTimeOrganizer – E2eFixtureSeeder.cs", "AdhdTimeOrganizer – TestSeedGate.cs"], ActiveSeconds: 60, IsMapped: true),
        new(StartHour: 11, Minutes: 25, "chrome.exe", "Google Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            ["GitHub – Pull requests", "Stack Overflow – EF Core partitioning"], ActiveSeconds: 45, IsMapped: false),
        new(StartHour: 15, Minutes: 20, "Code.exe", "Visual Studio Code", @"C:\Program Files\Microsoft VS Code\Code.exe",
            ["adhd-time-organizer – global-setup.ts"], ActiveSeconds: 50, IsMapped: false),
        new(StartHour: 20, Minutes: 15, "vlc.exe", "VLC media player", @"C:\Program Files\VideoLAN\VLC\vlc.exe",
            ["Documentary.mkv"], ActiveSeconds: 55, IsMapped: false)
    ];

    /// <summary>
    /// The Android ledger — whole sessions rather than minute windows, which is what the phone uploads.
    /// Five apps, so the distinct-entries grid and the app pie chart both have something to separate.
    /// </summary>
    private static readonly AndroidApp[] AndroidApps =
    [
        new("com.spotify.music", "Spotify", StartHour: 8, Minutes: 35),
        new("com.google.android.gm", "Gmail", StartHour: 10, Minutes: 12),
        new("com.android.chrome", "Chrome", StartHour: 12, Minutes: 28),
        new("com.whatsapp", "WhatsApp", StartHour: 17, Minutes: 18),
        new("com.duolingo", "Duolingo", StartHour: 21, Minutes: 15)
    ];

    /// <summary>The device every seeded Android session was uploaded from.</summary>
    private const string AndroidDeviceId = "e2e-pixel-7";

    /// <summary>
    /// Every table this seeder writes an explicit id into. Their sequences are advanced past the fixture
    /// band afterwards — see <see cref="AdvanceSequencesAsync"/>.
    /// </summary>
    /// <remarks>
    /// The three tracking ledgers and the routine completion ledger are deliberately absent: nothing addresses
    /// a row of theirs by id (the freeze action keys on <c>periodStart</c>), so they keep their generated ids
    /// and their sequences need no nudge.
    /// </remarks>
    private static readonly Type[] ExplicitIdEntities =
    [
        typeof(User), typeof(ActivityRole), typeof(ActivityCategory), typeof(Activity),
        typeof(TodoList), typeof(TodoListItem), typeof(ActivityHistory), typeof(PlannerTask),
        typeof(ActivityBacklogProfile), typeof(ActivityBucketListProfile), typeof(ActivityProjectProfile),
        typeof(MemoryAnchor), typeof(RoutineTodoList),
        typeof(TaskPlannerDayTemplate), typeof(TemplatePlannerTask),
        typeof(TrackerDesktopMappingByPattern), typeof(TrackerAndroidMappingByPattern),
        typeof(Notification),
        typeof(Reminder), typeof(ReminderDefinition), typeof(ReminderDispatch)
    ];

    public async Task<TestSeedResponse> ResetAsync(CancellationToken ct)
    {
        // One date for the whole reset. Reading the clock per row would let a seed that straddles
        // midnight UTC produce a fixture whose "yesterday" and "today" disagree with each other.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        logger.LogInformation("Resetting the e2e fixture ({Fixture}) against seed date {SeedDate}.", FixtureVersion, today);

        await TruncateAsync(ct);

        // Roles first, then the root admin — the ordinary production path, unchanged. Without it the
        // two fixture accounts would have no "User" role row to point at and every request they made
        // would 403 rather than 401, which reads as an endpoint bug.
        await appWideDefaults.SeedAllAsync();

        await CreateAccountAsync(PrimaryUserId, PrimaryUserEmail, PrimaryUserPassword,
            [PortalRoleCatalog.User], ct);
        await CreateAccountAsync(SecondaryUserId, SecondaryUserEmail, SecondaryUserPassword,
            [PortalRoleCatalog.User], ct);
        await CreateAccountAsync(AdminUserId, AdminUserEmail, AdminUserPassword,
            [PortalRoleCatalog.User, PortalRoleCatalog.Admin], ct);

        await SeedPrimaryAccountAsync(today, ct);
        await SeedSecondaryAccountAsync(today, ct);

        // After both accounts: the reminders span them, and the task-linked one needs its planner task.
        await AddRemindersAsync(today, ct);

        var scheduler = await UpsertOrphanedSchedulerJobAsync(today, ct);

        await AdvanceSequencesAsync(ct);

        var response = await BuildResponseAsync(today, scheduler, ct);

        logger.LogInformation(
            "E2e fixture ready: {ActivityCount} activities, {ItemCount} to-do items ({OverdueCount} overdue), " +
            "{HistoryCount} history rows, {PlannerCount} planner tasks.",
            response.Counts.Activities, response.Counts.TodoListItems, response.Counts.OverdueTodoListItems,
            response.Counts.ActivityHistoryEntries, response.Counts.PlannerTasks);

        return response;
    }

    // --- reset ----------------------------------------------------------------------------------

    /// <summary>
    /// One <c>TRUNCATE … RESTART IDENTITY CASCADE</c> over every mapped table, derived from the model
    /// so it cannot drift from the mapping. Schema-qualified for the reason
    /// <c>docs/claude/persistence.md</c> gives: hand-written SQL is not qualified for you, and an
    /// unqualified name resolves against the connection's <c>search_path</c>.
    /// </summary>
    /// <remarks>
    /// A single statement rather than one per table, so FK order does not have to be expressed
    /// anywhere: Postgres truncates the whole set together. <c>__EFMigrationsHistory</c> is not an
    /// entity and is therefore never in the list — migration state survives, as it must. Entities
    /// mapped to a view (the three <c>mv_*</c> suggestion-pattern views) are skipped; they are derived
    /// and get refreshed rather than truncated.
    /// </remarks>
    private async Task TruncateAsync(CancellationToken ct)
    {
        var defaultSchema = dbContext.Model.GetDefaultSchema() ?? ModuleSchemas.Shared;

        var targets = dbContext.Model.GetEntityTypes()
            .Where(entityType => entityType.GetViewName() is null)
            .Select(entityType => new
            {
                Schema = entityType.GetSchema() ?? defaultSchema,
                Table = entityType.GetTableName()
            })
            .Where(target => !string.IsNullOrEmpty(target.Table))
            // The scheduler's job registry and run ledger are operational, not fixture data, and the
            // registrars that repopulate them run once per boot — truncating here would leave the app
            // with no scheduled jobs until the next restart, silently.
            .Where(target => target.Schema != ModuleSchemas.Scheduler)
            .Select(target => $"\"{target.Schema}\".\"{target.Table}\"")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        // Both identifiers come from the EF model and cannot be parameterized, which is what makes
        // EF1002 a false positive here — the same exemption SeederExtensions.TruncateAsync takes.
#pragma warning disable EF1002
        await dbContext.Database.ExecuteSqlRawAsync(
            $"TRUNCATE TABLE {string.Join(", ", targets)} RESTART IDENTITY CASCADE", ct);
#pragma warning restore EF1002

        // TRUNCATE happens behind the ChangeTracker's back — anything tracked now describes rows that
        // no longer exist, and the next SaveChanges would try to update them.
        dbContext.ChangeTracker.Clear();

        var alarmJobs = await DeleteTimerAlarmJobsAsync(ct);

        logger.LogInformation("Truncated {TableCount} tables and deleted {AlarmJobs} timer-alarm job(s).", targets.Count, alarmJobs);
    }

    /// <summary>
    /// Deletes the scheduler jobs (and their runs) of every timer-boundary alarm — the one exception to leaving the
    /// scheduler schema alone.
    /// <para>
    /// An alarm's job key is derived from its row id (<c>history.timer-boundary.{id}</c>), and the truncate above
    /// restarts that id at 1. Left in place, the old registry row is <i>re-used</i> by the first alarm a spec
    /// schedules: same key, so the upsert flips a long-retired job back to Active and the new alarm inherits its
    /// run history. Unlike the recurring jobs the comment above protects, these rows describe alarm rows that no
    /// longer exist, and no registrar recreates them — there is nothing to lose. A trigger still armed in the
    /// in-memory store for one of them fires at an unknown key, which the dispatcher logs and skips.
    /// </para>
    /// </summary>
    private async Task<int> DeleteTimerAlarmJobsAsync(CancellationToken ct)
    {
        var alarmJobIds = dbContext.ScheduledJobs.IgnoreQueryFilters()
            .Where(job => job.HandlerKey == TimerBoundaryAlarmJobHandler.HandlerKey)
            .Select(job => job.Id);

        // Runs first: they hold the FK to the job. Every run of these jobs goes in one statement, so the
        // replay-lineage self-reference (Restrict) never points outside the deleted set.
        await dbContext.ScheduledJobRuns
            .Where(run => alarmJobIds.Contains(run.ScheduledJobId))
            .ExecuteDeleteAsync(ct);

        return await dbContext.ScheduledJobs.IgnoreQueryFilters()
            .Where(job => job.HandlerKey == TimerBoundaryAlarmJobHandler.HandlerKey)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Moves each explicitly-seeded table's sequence past the fixture band, so a row the app creates
    /// during a test run cannot be handed an id the fixture already used. Without it a sequence still
    /// sits at 1 after the reset and the very first activity a spec creates collides with
    /// <see cref="E2eFixtureIds.ActivityBugFixing"/>'s neighbours.
    /// </summary>
    private async Task AdvanceSequencesAsync(CancellationToken ct)
    {
        var defaultSchema = dbContext.Model.GetDefaultSchema() ?? ModuleSchemas.Shared;

        foreach (var clrType in ExplicitIdEntities)
        {
            var entityType = dbContext.Model.FindEntityType(clrType);
            var table = entityType?.GetTableName();
            if (string.IsNullOrEmpty(table))
                continue;

            var schema = entityType!.GetSchema() ?? defaultSchema;
            var qualified = $"\"{schema}\".\"{table}\"";

            // pg_get_serial_sequence resolves the sequence behind the column's DEFAULT, so this works
            // for both the serial columns BaseEntityConfigure creates and Identity's own user table.
            // GREATEST(..., 1) covers a table the fixture happens to leave empty; COALESCE covers the
            // MAX over no rows.
#pragma warning disable EF1002
            await dbContext.Database.ExecuteSqlRawAsync(
                $"""
                 SELECT setval(
                     pg_get_serial_sequence('{schema}.{table}', 'id'),
                     GREATEST(COALESCE((SELECT MAX(id) FROM {qualified}), 1), 1))
                 """, ct);
#pragma warning restore EF1002
        }
    }

    // --- accounts -------------------------------------------------------------------------------

    /// <summary>
    /// Creates one fixture account and its per-user defaults.
    /// </summary>
    /// <remarks>
    /// The password hash is written directly rather than through <c>UserManager.CreateAsync</c>, for two
    /// reasons. The credential the SPA's <c>e2e/.env.test</c> carries does not satisfy this app's own
    /// registration policy (<c>IdentityServiceExtensions</c>: 10 characters, upper, digit,
    /// non-alphanumeric), so <c>CreateAsync</c> would reject it — while sign-in, which only ever
    /// verifies the hash, accepts it perfectly. And <c>CreateAsync</c> chooses the id, which is the one
    /// thing this fixture must not delegate.
    /// <para>
    /// <c>Locale = En</c> is load-bearing, not cosmetic: <c>App.vue</c> drives <c>i18n.locale</c> off
    /// the persisted value, so a Slovak-defaulting account breaks every label-based locator in the
    /// suite. The seeded root admin stays <c>Sk</c> — it is not a test account.
    /// </para>
    /// </remarks>
    /// <param name="roles">
    /// The role names to grant, from <see cref="PortalRoleCatalog"/>. Every account gets <c>User</c>; the
    /// admin account gets <c>Admin</c> on top of it — see <see cref="E2eFixtureIds.AdminUserId"/> for why
    /// that is a third account rather than a promotion of the primary one. A name with no <c>AspNetRoles</c>
    /// row throws rather than silently granting nothing, because the symptom of the silent version is a 403
    /// on a route nobody expects to be gated.
    /// </param>
    private async Task CreateAccountAsync(long id, string email, string password, string[] roles, CancellationToken ct)
    {
        var normalized = email.ToUpperInvariant();

        var user = new User
        {
            Id = id,
            // The Email setter mirrors the value onto UserName; the normalized forms are not derived
            // for us outside UserManager, so both are set explicitly.
            Email = email,
            NormalizedEmail = normalized,
            NormalizedUserName = normalized,
            EmailConfirmed = true,
            // Derived from the id rather than Guid.NewGuid() so two resets produce the same row. Nothing
            // reads either value across a reset; determinism here is a property of the fixture, not a
            // requirement of Identity.
            SecurityStamp = $"e2e-security-stamp-{id}",
            ConcurrencyStamp = $"e2e-concurrency-stamp-{id}",
            Locale = AvailableLocales.En,
            Timezone = TimeZoneInfo.Utc,
            FirstDayOfWeek = 1
        };
        user.PasswordHash = passwordHasher.HashPassword(user, password);

        dbContext.Users.Add(user);

        // Every endpoint without explicit roles gets Roles("User","Admin","Root") from the global
        // configurator in Program.cs, so an account with no role row authenticates and then 403s on
        // everything it touches.
        var normalizedRoles = roles.Select(role => role.ToUpperInvariant()).ToList();

        var roleIds = await dbContext.Roles
            .Where(role => normalizedRoles.Contains(role.NormalizedName!))
            .Select(role => role.Id)
            .ToListAsync(ct);

        if (roleIds.Count != roles.Length)
            throw new InvalidOperationException(
                $"Fixture account {id} asked for {roles.Length} role(s) but only {roleIds.Count} exist. " +
                "The app-wide role seeder and PortalRoleCatalog have diverged; the fixture is incomplete.");

        foreach (var roleId in roleIds)
            dbContext.UserRoles.Add(new IdentityUserRole<long> { UserId = id, RoleId = roleId });

        await dbContext.SaveChangesAsync(ct);

        // The real sign-up path: activity roles, the four leisure lookups, timer presets, task
        // priorities, task importances, routine time periods, planner settings and two years of
        // calendar days. Their ids are auto-generated and are reported in the seed response rather
        // than pinned here.
        var defaults = await userDefaults.CreateDefaultsAsync(id, ct);
        if (defaults.Failed)
            throw new InvalidOperationException(
                $"Could not create the per-user defaults for fixture account {id}. The fixture is incomplete.");
    }

    // --- the primary account --------------------------------------------------------------------

    private async Task SeedPrimaryAccountAsync(DateOnly today, CancellationToken ct)
    {
        const long userId = PrimaryUserId;

        AddActivityRoles(userId, [
            (PrimaryRoleWork, "Work", ColorPalette.Blue, "fas fa-briefcase"),
            (PrimaryRoleHealth, "Health & Fitness", ColorPalette.Emerald, "fas fa-heart-pulse"),
            (PrimaryRoleLeisure, "Hobbies & Leisure", ColorPalette.Violet, "fas fa-guitar")
        ]);

        AddActivityCategories(userId, [
            (PrimaryCategoryDeepWork, "Deep Work", ColorPalette.Blue),
            (PrimaryCategoryQuickTasks, "Quick Tasks", ColorPalette.Sky),
            (PrimaryCategoryErrands, "Errands", ColorPalette.Yellow)
        ]);

        foreach (var entry in CalibrationSet)
            AddActivity(entry.ActivityId, userId, entry.Name, entry.RoleId, entry.CategoryId);

        AddActivity(ActivityReadANovel, userId, "Read a Novel", PrimaryRoleLeisure, null);
        AddActivity(ActivityLearnBouldering, userId, "Learn Bouldering", PrimaryRoleLeisure, null);
        AddActivity(ActivityBuildABookshelf, userId, "Build a Bookshelf", PrimaryRoleLeisure, null);

        // Archived: still named on every row that points at it, gone from every picker.
        AddActivity(ActivityRetiredHobby, userId, "Retired Hobby", PrimaryRoleLeisure, null, isArchived: true);

        // The canDelete pair. WriteBlogPost picks up one history row below and nothing else, so the
        // activity grid reports canDelete = false; UnusedIdea is referenced by nothing at all.
        AddActivity(ActivityWriteBlogPost, userId, "Write Blog Post", PrimaryRoleWork, PrimaryCategoryDeepWork);
        AddActivity(ActivityUnusedIdea, userId, "Unused Idea", PrimaryRoleWork, null);

        foreach (var entry in OverdueSet)
            AddActivity(entry.ActivityId, userId, entry.Name, entry.RoleId, PrimaryCategoryErrands);

        AddActivity(ActivityDeepCleanKitchen, userId, "Deep Clean Kitchen", PrimaryRoleHealth, PrimaryCategoryErrands);

        // Desktop tracking attributes to this one and nothing else does — see the id's own remarks.
        AddActivity(ActivityDeskResearch, userId, "Desk Research", PrimaryRoleWork, PrimaryCategoryDeepWork);

        AddActivity(ActivityTakeMedication, userId, "Take Medication", PrimaryRoleHealth, null);
        AddActivity(ActivityWeeklyReview, userId, "Weekly Review", PrimaryRoleWork, PrimaryCategoryQuickTasks);
        AddActivity(ActivityPayRent, userId, "Pay Rent", PrimaryRoleHealth, PrimaryCategoryErrands);

        // The four milestone routines' activities.
        AddActivity(ActivityDrinkWater, userId, "Drink Water", PrimaryRoleHealth, null);
        AddActivity(ActivityJournal, userId, "Journal", PrimaryRoleLeisure, null);
        AddActivity(ActivityMakeTheBed, userId, "Make the Bed", PrimaryRoleHealth, PrimaryCategoryQuickTasks);
        AddActivity(ActivityBrushTeeth, userId, "Brush Teeth", PrimaryRoleHealth, null);

        dbContext.TodoLists.AddRange(
            new TodoList { Id = PrimaryListFocus, UserId = userId, Name = "Focus", Text = "What I am working on now", Icon = "fas fa-bullseye" },
            new TodoList { Id = PrimaryListErrands, UserId = userId, Name = "Errands", Text = "Off-desk things that keep slipping", Icon = "fas fa-bag-shopping" },
            // Deliberately never given an item, so the list empty-state is reachable.
            new TodoList { Id = PrimaryListSomeday, UserId = userId, Name = "Someday", Text = "Nothing here yet", Icon = "fas fa-cloud" });

        // Roles, categories, activities and lists have to exist before the rows that FK into them.
        await dbContext.SaveChangesAsync(ct);

        var priorities = await PriorityIdsAsync(userId, ct);

        AddCalibrationItems(userId, today, priorities);
        AddOverdueItems(userId, today, priorities);
        AddRemainingItems(userId, today, priorities);
        AddCalibrationHistory(userId, today);

        // The one row that makes ActivityWriteBlogPost undeletable.
        dbContext.ActivityHistories.Add(new ActivityHistory
        {
            Id = HistoryWriteBlogPost,
            UserId = userId,
            ActivityId = ActivityWriteBlogPost,
            StartTimestamp = Utc(today.AddDays(-4), 20),
            EndTimestamp = Utc(today.AddDays(-4), 20).AddMinutes(50),
            Length = FromMinutes(50)
        });

        // The entities, not just their ids: the Daily and Weekly periods are mutated below (streak state,
        // reset instant, freeze window) and are tracked, so the same SaveChanges writes them back.
        var periods = await RoutineTimePeriodsAsync(userId, ct);

        AddRoutines(userId, today, periods);
        AddRoutinePeriodHistory(today, periods);
        AddLeisureProfiles(userId, today, await LookupIdsAsync(userId, ct));
        AddDayPlannerTemplates(userId, await ImportanceIdsAsync(userId, ct));
        AddTrackingMappings(userId);

        await dbContext.SaveChangesAsync(ct);

        // Planner tasks need the calendar rows CreateDefaultsAsync produced, so they come last.
        await AddPlannerTasksAsync(userId, today, ct);
        await dbContext.SaveChangesAsync(ct);

        // After the planner tasks: one notification deep-links to the first of them.
        await AddTrackingLedgersAsync(userId, today, ct);
        await AddNotificationsAsync(userId, today, periods, ct);
    }

    /// <summary>
    /// The six items whose activities carry history. Each lands in <see cref="E2eFixtureIds.PrimaryListFocus"/>
    /// on a distinct activity — <c>todo_list_item</c> is unique on (user, activity, list).
    /// </summary>
    private void AddCalibrationItems(long userId, DateOnly today, IReadOnlyDictionary<string, long> priorities)
    {
        // DisplayOrder descends by TodoListSettings.DisplayOrderGap from DisplayOrderStart
        // (MaxItems * gap = 100 000 000), which is exactly what GetNextDisplayOrder would have handed
        // out. Assigned rather than queried so the values are the same on every reset.
        var order = DisplayOrderStart;

        for (var index = 0; index < CalibrationSet.Length; index++)
        {
            var entry = CalibrationSet[index];

            var item = new TodoListItem
            {
                Id = entry.ItemId,
                UserId = userId,
                ActivityId = entry.ActivityId,
                TodoListId = PrimaryListFocus,
                TaskPriorityId = priorities[index < 2 ? "Today" : index < 5 ? "This week" : "This month"],
                IsDone = false,
                SuggestedTime = FromMinutes(entry.EstimateMinutes),
                Note = $"Estimated at {entry.EstimateMinutes} minutes.",
                DueDate = today.AddDays(index),
                DisplayOrder = order
            };

            // One of the six also carries steps, so the item detail view has a checklist to render.
            // DoneCount matches the single ticked step: an item whose counter and steps disagree is a
            // state no toggle path can produce.
            if (entry.ActivityId == ActivityBugFixing)
            {
                item.Steps =
                [
                    new TodoListStep { Id = StepBugFixingReproduce, Name = "Reproduce the login timeout locally", Order = 1, IsDone = true },
                    new TodoListStep { Id = StepBugFixingNullRef, Name = "Fix the null ref on the dashboard", Order = 2, IsDone = false },
                    new TodoListStep { Id = StepBugFixingRegression, Name = "Write a regression test", Order = 3, IsDone = false }
                ];
                item.DoneCount = 1;
                item.TotalCount = 3;
            }

            dbContext.TodoListItems.Add(item);
            order -= DisplayOrderGap;
        }
    }

    /// <summary>
    /// Six not-done items between one and fourteen days past due, all in
    /// <see cref="E2eFixtureIds.PrimaryListErrands"/>.
    /// </summary>
    private void AddOverdueItems(long userId, DateOnly today, IReadOnlyDictionary<string, long> priorities)
    {
        var order = DisplayOrderStart;

        foreach (var entry in OverdueSet)
        {
            dbContext.TodoListItems.Add(new TodoListItem
            {
                Id = entry.ItemId,
                UserId = userId,
                ActivityId = entry.ActivityId,
                TodoListId = PrimaryListErrands,
                TaskPriorityId = priorities["Today"],
                IsDone = false,
                DueDate = today.AddDays(-entry.DaysOverdue),
                DueTime = new TimeOnly(17, 0),
                Note = $"{entry.DaysOverdue} day(s) past due.",
                DisplayOrder = order
            });

            order -= DisplayOrderGap;
        }
    }

    private void AddRemainingItems(long userId, DateOnly today, IReadOnlyDictionary<string, long> priorities)
    {
        // Partially complete multi-count. TotalCount is constrained to 2..99 and DoneCount must not
        // exceed it (CK_TodoListItem_TotalCount_Range / _DoneCount_LessOrEqual_TotalCount).
        dbContext.TodoListItems.Add(new TodoListItem
        {
            Id = ItemDeepCleanKitchen,
            UserId = userId,
            ActivityId = ActivityDeepCleanKitchen,
            TodoListId = PrimaryListFocus,
            TaskPriorityId = priorities["This week"],
            IsDone = false,
            DoneCount = 2,
            TotalCount = 5,
            Note = "One room at a time.",
            DueDate = today.AddDays(5),
            DisplayOrder = DisplayOrderStart - 6 * DisplayOrderGap
        });

        // Completed, so the daily recap has a row. CompletedTimestamp is deliberately NOT set here —
        // TodoListItemCompletionInterceptor stamps it on insert for an item created already-done, and
        // it is the only thing allowed to write that column.
        dbContext.TodoListItems.Add(new TodoListItem
        {
            Id = ItemCodeReviewDone,
            UserId = userId,
            ActivityId = ActivityCodeReview,
            TodoListId = PrimaryListErrands,
            TaskPriorityId = priorities["Today"],
            IsDone = true,
            DueDate = today,
            DisplayOrder = DisplayOrderStart - 6 * DisplayOrderGap
        });

        // Temptation bundling: a task paired with a leisure activity. PairedLeisureActivityId is an
        // Activity id, not a profile id.
        dbContext.TodoListItems.Add(new TodoListItem
        {
            Id = ItemBuildABookshelf,
            UserId = userId,
            ActivityId = ActivityBuildABookshelf,
            TodoListId = PrimaryListFocus,
            TaskPriorityId = priorities["This month"],
            IsDone = false,
            PairedLeisureActivityId = ActivityReadANovel,
            SuggestedTime = FromMinutes(90),
            Note = "Reward: an hour with the novel afterwards.",
            DueDate = today.AddDays(12),
            DisplayOrder = DisplayOrderStart - 7 * DisplayOrderGap
        });
    }

    /// <summary>
    /// Three history rows per calibration activity, on days −1, −3 and −7 at 09:00, 13:00 and 16:00.
    /// <c>activity_history</c> is unique on (user, activity, start), which the distinct days satisfy.
    /// The first of each three is linked back to its to-do item so the item-level attribution — the
    /// exact number the chip prefers over the activity average — is exercised too.
    /// </summary>
    private void AddCalibrationHistory(long userId, DateOnly today)
    {
        var daysAgo = new[] { 1, 3, 7 };
        var hours = new[] { 9, 13, 16 };
        var id = HistoryCalibrationFirst;

        foreach (var entry in CalibrationSet)
            for (var occurrence = 0; occurrence < daysAgo.Length; occurrence++)
            {
                var start = Utc(today.AddDays(-daysAgo[occurrence]), hours[occurrence]);
                var minutes = entry.ActualMinutes[occurrence];

                dbContext.ActivityHistories.Add(new ActivityHistory
                {
                    Id = id++,
                    UserId = userId,
                    ActivityId = entry.ActivityId,
                    StartTimestamp = start,
                    EndTimestamp = start.AddMinutes(minutes),
                    Length = FromMinutes(minutes),
                    TodoListItemId = occurrence == 0 ? entry.ItemId : null
                });
            }
    }

    /// <summary>
    /// Planner tasks across a known range: three today, two tomorrow, none on today+2, two on today+3.
    /// The gap is the point — <c>/day-planner/:date</c> has a populated state and an empty one, and
    /// both have to be reachable without a spec creating data first.
    /// </summary>
    private async Task AddPlannerTasksAsync(long userId, DateOnly today, CancellationToken ct)
    {
        var dates = new[] { today, today.AddDays(1), today.AddDays(3) };

        var calendarIdByDate = await dbContext.Calendars
            .IgnoreQueryFilters()
            .Where(calendar => calendar.UserId == userId && dates.Contains(calendar.Date))
            .ToDictionaryAsync(calendar => calendar.Date, calendar => calendar.Id, ct);

        var planned = new (long ActivityId, DateOnly Date, int StartHour, int Minutes, PlannerTaskStatus Status)[]
        {
            (ActivityMorningExercise, today, 7, 40, PlannerTaskStatus.Completed),
            (ActivityBugFixing, today, 9, 90, PlannerTaskStatus.InProgress),
            (ActivityCodeReview, today, 11, 30, PlannerTaskStatus.NotStarted),
            (ActivityFeatureDevelopment, today.AddDays(1), 9, 120, PlannerTaskStatus.NotStarted),
            (ActivityGroceryShopping, today.AddDays(1), 17, 60, PlannerTaskStatus.NotStarted),
            (ActivityMealPreparation, today.AddDays(3), 18, 75, PlannerTaskStatus.NotStarted),
            (ActivityBuildABookshelf, today.AddDays(3), 20, 60, PlannerTaskStatus.NotStarted)
        };

        var id = PlannerTaskFirst;

        foreach (var task in planned)
        {
            // A calendar day the per-user default seeder did not produce means the fixture is not what
            // it claims to be; failing here is better than a day-planner page that is silently empty.
            if (!calendarIdByDate.TryGetValue(task.Date, out var calendarId))
                throw new InvalidOperationException(
                    $"No calendar row exists for {task.Date:O} and fixture account {userId}; the planner fixture cannot be built.");

            var start = new TimeOnly(task.StartHour, 0);

            dbContext.PlannerTasks.Add(new PlannerTask
            {
                Id = id++,
                UserId = userId,
                ActivityId = task.ActivityId,
                CalendarId = calendarId,
                StartTime = start,
                EndTime = start.AddMinutes(task.Minutes),
                IsBackground = false,
                Status = task.Status,
                ActualStartTime = task.Status == PlannerTaskStatus.Completed ? start : null,
                ActualEndTime = task.Status == PlannerTaskStatus.Completed ? start.AddMinutes(task.Minutes) : null,
                Location = task.StartHour < 12 ? "Home" : "Office"
            });
        }
    }

    /// <summary>
    /// One profile per kind, on three distinct activities, plus the memory anchor that closes the loop
    /// on the bucket-list entry. A bucket-list entry is inherently one-time — unlike a backlog profile
    /// it carries no <c>IsRepeatable</c> — which is what makes it the right one to anchor.
    /// </summary>
    private void AddLeisureProfiles(long userId, DateOnly today, LookupIds lookups)
    {
        dbContext.Set<ActivityBacklogProfile>().Add(new ActivityBacklogProfile
        {
            Id = BacklogProfileReadANovel,
            ActivityId = ActivityReadANovel,
            LocationTypeId = lookups.LocationTypeIndoor,
            WeatherDependencyId = lookups.WeatherDependencyNone,
            ExpectedCostTierId = lookups.CostTierFree,
            EnergyLevel = EnergyLevel.Low,
            EffortType = EffortType.Mental,
            MinParticipants = 1,
            MaxParticipants = 1,
            DurationMinutes = 45,
            IsRepeatable = true
        });

        dbContext.Set<ActivityBucketListProfile>().Add(new ActivityBucketListProfile
        {
            Id = BucketListProfileLearnBouldering,
            ActivityId = ActivityLearnBouldering,
            ExperienceTypeId = lookups.ExperienceTypeSkill,
            ComfortZoneStep = 3,
            RequiresTravel = false,
            FinancialGoal = 120.00m,
            InspirationSource = "A friend's climbing photos"
        });

        dbContext.Set<ActivityProjectProfile>().Add(new ActivityProjectProfile
        {
            Id = ProjectProfileBuildABookshelf,
            ActivityId = ActivityBuildABookshelf,
            DifficultyLevel = DifficultyLevel.Intermediate,
            ProjectArea = "Woodworking",
            EstimatedHours = 8.50m,
            IsMessy = true,
            MaterialsNeeded = ["Pine boards", "Wood glue", "Sandpaper"],
            RequiredTools = ["Drill", "Circular saw", "Clamps"],
            ReadinessStatus = ReadinessStatus.NeedsShopping
        });

        // Last month, so the anchor is always in the past without being a hardcoded calendar date.
        var anchoredMonth = today.AddMonths(-1);

        dbContext.Set<MemoryAnchor>().Add(new MemoryAnchor
        {
            Id = MemoryAnchorLearnBouldering,
            UserId = userId,
            ActivityId = ActivityLearnBouldering,
            AnchorMonth = anchoredMonth.Month,
            AnchorYear = anchoredMonth.Year,
            HighlightNote = "Topped a V3 on the overhang for the first time.",
            Rating = 9
        });

        // An anchor on the ARCHIVED activity. The anchor grid is the one profile surface that must keep
        // showing a row whose activity is archived — anchoring is for things already done, and retiring the
        // activity afterwards is the normal path, not an edge case. The grid lifts
        // AppQueryFilters.ActivityArchived for exactly this row; without a row of this shape in the fixture
        // nothing exercised the lift, and the bug it fixes was invisible on screen (an empty grid reporting
        // itemsCount: 1) rather than an error.
        dbContext.Set<MemoryAnchor>().Add(new MemoryAnchor
        {
            Id = MemoryAnchorRetiredHobby,
            UserId = userId,
            ActivityId = ActivityRetiredHobby,
            AnchorMonth = anchoredMonth.AddMonths(-1).Month,
            AnchorYear = anchoredMonth.AddMonths(-1).Year,
            HighlightNote = "The last session before hanging it up for good.",
            Rating = 7
        });
    }

    /// <summary>
    /// The routines. Three across the periods with item streaks in three different states — running, just
    /// started, and never completed — plus four more daily ones whose streaks sit in the four milestone tiers
    /// above the first (<c>RoutineTodoListItem.vue</c> switches its icon at 7 / 30 / 90 / 180 / 365 on the
    /// item's <i>current</i> streak, so <c>BestStreak = 30</c> on the first item never reached it).
    /// </summary>
    /// <remarks>
    /// Every item's reset and completion instants are placed relative to the cycle its period is actually
    /// in — <c>LastResetDate</c> on the boundary <see cref="AddRoutinePeriodHistory"/> stamps as
    /// <c>LastResetAt</c>, <c>LastCompletedAt</c> inside the right cycle — and every item done in the open cycle
    /// carries <c>BestStreakBeforeScore</c>, the mark <c>RoutineResetService</c> reads as "already counted this
    /// cycle". An item done without the mark double-scores the next tick; one carrying it while undone refuses
    /// to score at all and gives a point back on the next un-tick.
    /// </remarks>
    private void AddRoutines(long userId, DateOnly today, IReadOnlyDictionary<string, RoutineTimePeriod> periods)
    {
        var never = Utc(today.AddYears(-1), 0);
        var everyDay = new List<DayOfWeek>
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
            DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
        };

        // The daily cycle opened at midnight today; the weekly one on the most recent Monday (the period's
        // ResetAnchorDay), which may be today.
        var dailyCycleStart = today;
        var weeklyCycleStart = LastWeeklyBoundary(today, periods["Weekly"]);

        var daily = periods["Daily"].Id;

        dbContext.RoutineTodoLists.AddRange(
            new RoutineTodoList
            {
                Id = RoutineTakeMedication,
                UserId = userId,
                ActivityId = ActivityTakeMedication,
                TimePeriodId = daily,
                IsDone = true,
                DisplayOrder = DisplayOrderStart,
                // Twelve including today's tick, so eleven closed cycles — the Daily period's own streak.
                Streak = 12,
                BestStreak = 30,
                LastResetDate = dailyCycleStart,
                LastCompletedAt = Utc(today, 8),
                // Scored today, well short of the record: an un-tick gives back 12 → 11 and leaves 30 alone.
                BestStreakBeforeScore = 30,
                SuggestedDays = [.. everyDay]
            },
            new RoutineTodoList
            {
                Id = RoutineWeeklyReview,
                UserId = userId,
                ActivityId = ActivityWeeklyReview,
                TimePeriodId = periods["Weekly"].Id,
                IsDone = false,
                DisplayOrder = DisplayOrderStart,
                Streak = 1,
                BestStreak = 4,
                LastResetDate = weeklyCycleStart,
                // The Sunday before the cycle opened: inside the cycle that closed last, which is the one
                // the streak of 1 is for, and never inside the open one whichever weekday today is.
                LastCompletedAt = Utc(weeklyCycleStart.AddDays(-1), 18),
                SuggestedDays = [DayOfWeek.Sunday]
            },
            new RoutineTodoList
            {
                Id = RoutinePayRent,
                UserId = userId,
                ActivityId = ActivityPayRent,
                TimePeriodId = periods["Monthly"].Id,
                IsDone = false,
                DisplayOrder = DisplayOrderStart,
                Streak = 0,
                BestStreak = 0,
                LastCompletedAt = never,
                SuggestedDayOfMonth = 1
            });

        // The milestone tiers, one item each. Streaks sit well past their tier, so a spec that un-ticks and
        // re-ticks one (net zero on the streak) cannot drop it across a boundary. A cycle that lapses undone
        // is different: it zeroes the item's streak outright, so a spec straddling midnight UTC with one of
        // these left un-ticked starts its next assertion from the lowest tier, not the seeded one.
        var milestones = new (long Id, long ActivityId, int Streak, bool DoneToday)[]
        {
            (RoutineDrinkWater, ActivityDrinkWater, 45, false),
            (RoutineJournal, ActivityJournal, 120, true),
            (RoutineMakeTheBed, ActivityMakeTheBed, 200, true),
            (RoutineBrushTeeth, ActivityBrushTeeth, 400, true)
        };

        var order = DisplayOrderStart;

        foreach (var milestone in milestones)
        {
            order -= DisplayOrderGap;

            dbContext.RoutineTodoLists.Add(new RoutineTodoList
            {
                Id = milestone.Id,
                UserId = userId,
                ActivityId = milestone.ActivityId,
                TimePeriodId = daily,
                IsDone = milestone.DoneToday,
                DisplayOrder = order,
                Streak = milestone.Streak,
                BestStreak = milestone.Streak,
                LastResetDate = dailyCycleStart,
                // Done today has scored this cycle. Not yet done was last ticked yesterday evening and is
                // one tick away from extending — the state a spec toggling it wants to start from.
                LastCompletedAt = milestone.DoneToday ? Utc(today, 7) : Utc(today.AddDays(-1), 20),
                // Streak == BestStreak, so today's tick is the one that set the record: un-ticking gives back
                // both, to Streak - 1 / Streak - 1.
                BestStreakBeforeScore = milestone.DoneToday ? milestone.Streak - 1 : null,
                SuggestedDays = [.. everyDay]
            });
        }
    }

    /// <summary>
    /// The pattern of the Weekly period's last <see cref="E2eFixtureIds.RoutineHistoryCycles"/> closed cycles,
    /// newest first: <c>'x'</c> met the threshold, <c>'.'</c> missed it, <c>'f'</c> missed it and had a streak
    /// freeze spent on it.
    /// <para>
    /// Written to agree with the Weekly Review item's own counters (streak 1, best 4): the newest cycle is a
    /// hit and the one before it is not, and the longest unbroken run is four. The freeze sits on that
    /// most recent miss so the heatmap's third cell state is in view rather than scrolled off, and the miss
    /// behind it is left unfrozen so a spec still has one to spend a freeze on.
    /// </para>
    /// </summary>
    private const string WeeklyHistoryPattern = "xf.xxxx.xx.xx.xx";

    /// <summary>
    /// The closed cycle in the Daily period's history that the first item missed. Eleven, because the item's
    /// streak of 12 includes today's still-open cycle — so the period, which only scores closed ones, is on 11.
    /// </summary>
    private const int DailyMissedCyclesAgo = 11;

    /// <summary>
    /// The routine <i>period</i> history: the completion ledger for the Daily and Weekly periods, and the
    /// period-level streak state that ledger would have left behind. Everything the heatmaps, the trend arrow,
    /// the consistency figure and the personal-bests panel read; none of it existed before <c>e2e-v4</c>,
    /// because the per-user default seeder creates the four periods bare and nothing had ever closed a cycle.
    /// </summary>
    /// <remarks>
    /// <para><b>Written to the state the reset job would have left, not merely to plausible values.</b> The
    /// grouped read (<c>GetAllGroupedRoutineTodoListEndpoint</c>) runs <c>RoutineResetService.TryResetCatchUp</c>
    /// on every period before rendering it. Seed a period whose <c>LastResetAt</c> makes it look due and the
    /// first request closes the cycle: it appends its own completion row, re-scores the streak, and un-ticks
    /// every item — silently, on a 200. So <c>LastResetAt</c> lands exactly on the boundary that opened the
    /// current cycle, the newest completion row ends on that same boundary, and the freeze window is stamped
    /// so <c>RefreshFreezeBudget</c> has nothing to refill. <c>TestSeedEndpointTests</c> asserts the catch-up
    /// is a no-op over the seeded rows.</para>
    /// <para>The period streaks are the ones <c>RoutineStreakFreezeService.RecomputeStreak</c> derives from
    /// the rows (it only ever raises, so a seeded value below the walk would be corrected on the first freeze
    /// and a spec would see the number change under it). <c>BestStreak</c> on the Daily period is 30 rather
    /// than the walk's 11 because its history is sixteen rows deep and the best run predates them — the same
    /// reason that method never lowers.</para>
    /// <para>Counts are honest: a frozen row keeps its 0 of 1, because a freeze changes how a period is
    /// scored, not what happened in it — the consistency figure is computed over the counts, the streak over
    /// the flags.</para>
    /// </remarks>
    private void AddRoutinePeriodHistory(DateOnly today, IReadOnlyDictionary<string, RoutineTimePeriod> periods)
    {
        var daily = periods["Daily"];
        var weekly = periods["Weekly"];

        // The freeze window: what RefreshFreezeBudget would open on first read. Stamped on all four periods so
        // no read of the fixture writes, and so the freeze count a spec sees is the one seeded here.
        var freezeWindowResetsAt = RoutineStreakFreezeService.NextBudgetReset(Utc(today, 0));
        foreach (var period in periods.Values)
        {
            period.FreezesRemaining = period.FreezeBudget;
            period.FreezeBudgetResetsAt = freezeWindowResetsAt;
        }

        // --- Daily: five items, one miss eleven cycles back ---------------------------------------------
        // Item counts are the five daily routines. All five are done on every closed cycle but one, where the
        // first item was missed — 4 of 5 is 80% against a threshold of 100, so that cycle broke the run.
        const int dailyItems = 5;

        daily.LastResetAt = Utc(today, 0);
        daily.Streak = DailyMissedCyclesAgo;
        daily.BestStreak = 30;

        for (var cyclesAgo = 0; cyclesAgo < RoutineHistoryCycles; cyclesAgo++)
        {
            var periodEnd = today.AddDays(-cyclesAgo);
            dbContext.Set<RoutinePeriodCompletion>().Add(new RoutinePeriodCompletion
            {
                TimePeriodId = daily.Id,
                PeriodStart = periodEnd.AddDays(-daily.LengthInDays),
                PeriodEnd = periodEnd,
                CompletedCount = cyclesAgo == DailyMissedCyclesAgo ? dailyItems - 1 : dailyItems,
                TotalCount = dailyItems,
                // The sweep's instant: the boundary itself, since the job runs at midnight UTC.
                CreatedAt = Utc(periodEnd, 0)
            });
        }

        // --- Weekly: one item, a mixed run, one freeze ---------------------------------------------------
        // The pattern is the Weekly depth; the constant is what the ids file and the docs promise. Two sources
        // that can drift apart get checked against each other rather than trusted.
        if (WeeklyHistoryPattern.Length != RoutineHistoryCycles)
            throw new InvalidOperationException(
                $"WeeklyHistoryPattern has {WeeklyHistoryPattern.Length} cycles but RoutineHistoryCycles is {RoutineHistoryCycles}.");

        var weeklyCycleStart = LastWeeklyBoundary(today, weekly);
        weekly.LastResetAt = Utc(weeklyCycleStart, 0);

        var frozenThisWindow = false;
        var rowsByPeriodStartAsc = new List<RoutinePeriodCompletion>();

        for (var cyclesAgo = 0; cyclesAgo < WeeklyHistoryPattern.Length; cyclesAgo++)
        {
            var periodEnd = weeklyCycleStart.AddDays(-weekly.LengthInDays * cyclesAgo);
            var mark = WeeklyHistoryPattern[cyclesAgo];
            var isFrozen = mark == 'f';

            // Spent the morning after the miss showed up, which is when the client offers the action.
            var frozenAt = isFrozen ? Utc(periodEnd, 9) : (DateTime?)null;
            frozenThisWindow |= frozenAt >= new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            rowsByPeriodStartAsc.Insert(0, new RoutinePeriodCompletion
            {
                TimePeriodId = weekly.Id,
                PeriodStart = periodEnd.AddDays(-weekly.LengthInDays),
                PeriodEnd = periodEnd,
                CompletedCount = mark == 'x' ? 1 : 0,
                TotalCount = 1,
                CreatedAt = Utc(periodEnd, 0),
                IsFrozen = isFrozen,
                FrozenAt = frozenAt
            });
        }

        dbContext.Set<RoutinePeriodCompletion>().AddRange(rowsByPeriodStartAsc);

        // A freeze spent inside the current calendar month is one the window has not yet given back.
        if (frozenThisWindow)
            weekly.FreezesRemaining--;

        // Derived by the same walk the freeze endpoint runs, from the rows just written — not typed in beside
        // them, so the pattern above is the single place the Weekly numbers come from.
        weekly.Streak = 0;
        weekly.BestStreak = 0;
        RoutineStreakFreezeService.RecomputeStreak(weekly, rowsByPeriodStartAsc);
    }

    /// <summary>
    /// The most recent instant a weekly-anchored period reset on or before <paramref name="today"/> — midnight on
    /// the last <c>ResetAnchorDay</c> weekday, which is today if today is that weekday. The instant
    /// <c>RoutineResetService.ComputeNextReset</c> would advance from to land strictly after now.
    /// </summary>
    private static DateOnly LastWeeklyBoundary(DateOnly today, RoutineTimePeriod period)
    {
        // The service's own convention: 1 = Monday … 7 = Sunday.
        var anchor = period.ResetAnchorDay == 7 ? DayOfWeek.Sunday : (DayOfWeek)period.ResetAnchorDay;
        var daysSince = ((int)today.DayOfWeek - (int)anchor + 7) % 7;
        return today.AddDays(-daysSince);
    }

    // --- day-planner templates --------------------------------------------------------------------

    /// <summary>
    /// Three templates with tasks across the day. Two is the floor — <c>/day-planner/templates/split</c>
    /// compares one against another, so a single template leaves that view in the same unreachable state an
    /// empty list does — and the third carries the pin, so the list's pinned section renders too.
    /// </summary>
    /// <remarks>
    /// This is the one surface of the four an e2e spec could create for itself:
    /// <c>POST /api/task-planner-day-template</c> is an ordinary authenticated create. It is seeded anyway
    /// because the read-only views (<c>/templates</c>, <c>/templates/:id</c>, <c>/templates/split</c>) are
    /// documented by opening them, and a row that exists only because some spec ran leaves them empty for
    /// anyone who opens them by hand.
    /// </remarks>
    private void AddDayPlannerTemplates(long userId, IReadOnlyDictionary<int, long> importances)
    {
        dbContext.Set<TaskPlannerDayTemplate>().AddRange(
            new TaskPlannerDayTemplate
            {
                Id = TemplateHomeOffice,
                UserId = userId,
                Name = "Home Office",
                Description = "A full desk day without the commute",
                Icon = "fas fa-house-laptop",
                IsActive = true,
                DefaultWakeUpTime = new TimeOnly(7, 0),
                DefaultBedTime = new TimeOnly(23, 0),
                SuggestedForDayType = DayTypeEnum.Workday,
                ScheduledDays = [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday],
                SuggestedLocation = LocationEnum.Home,
                Tags = ["focused", "deep work"],
                UsageCount = 4
            },
            new TaskPlannerDayTemplate
            {
                Id = TemplateOfficeDay,
                UserId = userId,
                Name = "Office Day",
                Description = "On site, with the commute either side",
                Icon = "fas fa-building",
                IsActive = true,
                DefaultWakeUpTime = new TimeOnly(6, 30),
                DefaultBedTime = new TimeOnly(22, 30),
                SuggestedForDayType = DayTypeEnum.Workday,
                ScheduledDays = [DayOfWeek.Tuesday, DayOfWeek.Thursday],
                SuggestedLocation = LocationEnum.Office,
                Tags = ["commute", "meetings"],
                UsageCount = 2
            },
            new TaskPlannerDayTemplate
            {
                Id = TemplateRelaxedWeekend,
                UserId = userId,
                Name = "Relaxed Weekend",
                Description = "Errands in the morning, nothing after lunch",
                Icon = "fas fa-umbrella-beach",
                IsActive = true,
                DefaultWakeUpTime = new TimeOnly(9, 0),
                DefaultBedTime = new TimeOnly(23, 30),
                SuggestedForDayType = DayTypeEnum.Weekend,
                ScheduledDays = [DayOfWeek.Saturday, DayOfWeek.Sunday],
                SuggestedLocation = LocationEnum.Home,
                Tags = ["relaxed"],
                IsPinned = true,
                UsageCount = 7
            });

        var critical = importances[TaskImportance.CriticalMarkerValue];
        var optional = importances[TaskImportance.OptionalMarkerValue];

        // (template, activity, start hour:minute, minutes, importance) — laid out so each template reads as
        // a plausible day rather than three tasks in a row, since these are what the template detail view and
        // the split comparison actually render.
        var tasks = new (long TemplateId, long ActivityId, int Hour, int Minute, int Minutes, long? ImportanceId, string Location)[]
        {
            (TemplateHomeOffice, ActivityMorningExercise, 7, 30, 40, null, "Home"),
            (TemplateHomeOffice, ActivityBugFixing, 9, 0, 90, critical, "Home"),
            (TemplateHomeOffice, ActivityCodeReview, 11, 0, 30, null, "Home"),
            (TemplateHomeOffice, ActivityMealPreparation, 12, 30, 45, null, "Home"),
            (TemplateHomeOffice, ActivityFeatureDevelopment, 14, 0, 120, optional, "Home"),

            (TemplateOfficeDay, ActivityMorningExercise, 6, 45, 30, null, "Gym"),
            (TemplateOfficeDay, ActivityCodeReview, 9, 30, 45, critical, "Office"),
            (TemplateOfficeDay, ActivityFeatureDevelopment, 10, 30, 150, null, "Office"),
            (TemplateOfficeDay, ActivityDeskResearch, 15, 0, 60, optional, "Office"),

            (TemplateRelaxedWeekend, ActivityGroceryShopping, 10, 0, 60, null, "Town"),
            (TemplateRelaxedWeekend, ActivityDeepCleanKitchen, 11, 30, 90, optional, "Home"),
            (TemplateRelaxedWeekend, ActivityReadANovel, 15, 0, 75, null, "Home")
        };

        var id = TemplateTaskFirst;

        foreach (var task in tasks)
        {
            var start = new TimeOnly(task.Hour, task.Minute);

            dbContext.Set<TemplatePlannerTask>().Add(new TemplatePlannerTask
            {
                Id = id++,
                UserId = userId,
                TemplateId = task.TemplateId,
                ActivityId = task.ActivityId,
                StartTime = start,
                EndTime = start.AddMinutes(task.Minutes),
                IsBackground = false,
                ImportanceId = task.ImportanceId,
                Location = task.Location
            });
        }

        // One background task, on the pinned template because no spec counts that template's tasks (Home
        // Office's 5 and Office Day's 4 are both asserted). Without it the background lane and the header's
        // "(+N background)" suffix render only for a template some spec created and deleted again.
        // Overlaps the kitchen clean, since a background task is one that runs alongside a foreground one.
        dbContext.Set<TemplatePlannerTask>().Add(new TemplatePlannerTask
        {
            Id = id,
            UserId = userId,
            TemplateId = TemplateRelaxedWeekend,
            ActivityId = ActivityMealPreparation,
            StartTime = new TimeOnly(11, 0),
            EndTime = new TimeOnly(14, 0),
            IsBackground = true,
            Location = "Home"
        });
    }

    // --- activity tracking ------------------------------------------------------------------------

    /// <summary>
    /// The two pattern-mapping kinds per platform, so the <i>Mappings</i> tab beside each distinct-entries
    /// grid is not empty and the "to activity" / "to ignored" workflow has both outcomes on screen.
    /// </summary>
    /// <remarks>
    /// ⚠ The two rules are not interchangeable, and which entries exist alongside them is part of the claim:
    /// <list type="bullet">
    /// <item>The <b>activity</b> rule matches <c>rider64.exe</c>, which the ledger does hold — and the
    /// seconds it matches are attributed in <see cref="AddTrackingLedgersAsync"/> through the same seam the
    /// heartbeat uses. A rule that resolves to an activity but leaves no attribution behind is a state the
    /// ingest path cannot produce.</item>
    /// <item>The <b>ignore</b> rule matches a process the ledger holds nothing for, for the mirror-image
    /// reason: the heartbeat drops an entry an ignore rule matches before it ever writes a row.</item>
    /// </list>
    /// </remarks>
    private void AddTrackingMappings(long userId)
    {
        dbContext.Set<TrackerDesktopMappingByPattern>().AddRange(
            new TrackerDesktopMappingByPattern
            {
                Id = DesktopMappingRider,
                UserId = userId,
                ProcessName = "rider64.exe",
                ProcessNameMatchType = PatternMatchType.Exact,
                IsActive = true,
                // Exactly one target group may be set (CK_TrackerDesktopMappingByPattern_TargetRequired):
                // an activity here, so Role, Category and IsIgnored all stay null.
                ActivityId = ActivityDeskResearch
            },
            new TrackerDesktopMappingByPattern
            {
                Id = DesktopMappingIgnoredLauncher,
                UserId = userId,
                ProcessName = "SteamLauncher.exe",
                ProcessNameMatchType = PatternMatchType.Exact,
                IsActive = true,
                // Never false — CK_TrackerDesktopMappingByPattern_IsIgnoredOnlyTrue allows only NULL or TRUE.
                IsIgnored = true
            });

        dbContext.Set<TrackerAndroidMappingByPattern>().AddRange(
            new TrackerAndroidMappingByPattern
            {
                Id = AndroidMappingSpotify,
                UserId = userId,
                PackageName = "com.spotify.music",
                PackageNameMatchType = PatternMatchType.Exact,
                IsActive = true,
                ActivityId = ActivityReadANovel
            },
            new TrackerAndroidMappingByPattern
            {
                Id = AndroidMappingIgnoredSettings,
                UserId = userId,
                PackageName = "com.android.settings",
                PackageNameMatchType = PatternMatchType.Exact,
                IsActive = true,
                IsIgnored = true
            });
    }

    /// <summary>
    /// The three ingest ledgers, seven days deep: browser windows, desktop windows and Android sessions.
    /// Without these every dashboard in <c>/activity-tracking</c> renders its whole control surface over
    /// "No activity recorded for this period", and both <c>settings/distinctEntries</c> grids are empty.
    /// </summary>
    /// <remarks>
    /// <para><b>Written as rows, not posted through the heartbeat endpoints.</b> The three ingest routes sit
    /// behind <c>PortalAuthorizationPolicies.ActivityTracking</c>, which requires the extension role — the
    /// seeder has no principal at all, so there is no token it could present — and their logic lives inside
    /// the endpoint handlers rather than in a service anything else can call. What the ingest path would have
    /// added is reproduced here instead: minute-aligned window starts (the timeline stitches on
    /// <c>WindowStart == previous end</c>), one row per unique-index key, ignore rules honoured by omission,
    /// and the attribution below.</para>
    /// <para>Nothing is derived downstream — both distinct-entries grids are a <c>GROUP BY</c> over these
    /// same tables at read time, so there is no second table that only the pipeline could have filled.</para>
    /// </remarks>
    private async Task AddTrackingLedgersAsync(long userId, DateOnly today, CancellationToken ct)
    {
        var webEntries = new List<WebExtensionActivityEntry>();
        var desktopEntries = new List<DesktopActivityEntry>();
        var androidSessions = new List<AndroidSessionData>();

        // Oldest first, so the generated ids ascend with time the way a real ledger's do.
        for (var daysAgo = TrackingDays - 1; daysAgo >= 0; daysAgo--)
        {
            var date = today.AddDays(-daysAgo);

            foreach (var block in WebBlocks)
                for (var minute = 0; minute < block.Minutes; minute++)
                {
                    var windowStart = Utc(date, block.StartHour).AddMinutes(minute);
                    var isLast = minute == block.Minutes - 1;

                    webEntries.Add(new WebExtensionActivityEntry
                    {
                        UserId = userId,
                        WindowStart = windowStart,
                        Domain = block.PrimaryDomain,
                        Url = block.PrimaryUrl,
                        // Varied rather than flat, and derived from the index rather than drawn at random:
                        // a chart whose every bar is the same height proves nothing, and a random one is a
                        // different fixture on every reset.
                        ActiveSeconds = 40 + minute * 7 % 21,
                        BackgroundSeconds = 0,
                        IsFinal = isLast
                    });

                    webEntries.Add(new WebExtensionActivityEntry
                    {
                        UserId = userId,
                        WindowStart = windowStart,
                        Domain = block.BackgroundDomain,
                        // Domain-only tracking: a legitimate entry the extension produces whenever no page
                        // URL was captured, and the reason WebExtensionActivityEntry.Url is nullable.
                        Url = null,
                        ActiveSeconds = 0,
                        BackgroundSeconds = 10 + minute * 11 % 21,
                        IsFinal = isLast
                    });
                }

            foreach (var block in DesktopBlocks)
            {
                for (var minute = 0; minute < block.Minutes; minute++)
                    desktopEntries.Add(new DesktopActivityEntry
                    {
                        UserId = userId,
                        WindowStart = Utc(date, block.StartHour).AddMinutes(minute),
                        ProcessName = block.ProcessName,
                        ProductName = block.ProductName,
                        // Held for ten minutes at a time, so each title is a session rather than a flicker —
                        // and so the distinct-entries grid has more rows than it has processes.
                        WindowTitle = block.WindowTitles[minute / 10 % block.WindowTitles.Length],
                        ExecutablePath = block.ExecutablePath,
                        IsFullscreen = false,
                        ActiveSeconds = block.ActiveSeconds,
                        BackgroundSeconds = 60 - block.ActiveSeconds,
                        IsPlayingSound = block.ProcessName == "vlc.exe",
                        ActiveMonitor = 1
                    });

                if (!block.IsMapped)
                    continue;

                // The attribution the desktop heartbeat performs for a mapped entry, through Core's seam —
                // the same call DesktopActivityHeartbeatEndpoint makes, and the reason the mapped block runs
                // at a full 60 active seconds per window: the sink extends a record whose end meets the next
                // window's start, so the block collapses to one ledger row exactly as it would in production.
                // Passed as the whole block rather than minute by minute because the sink reads the stored
                // rows to find what to extend, and nothing this method adds is saved yet.
                await attributionSink.AttributeAsync(
                    dbContext, userId, ActivityDeskResearch, Utc(date, block.StartHour), block.Minutes * 60, ct);
            }

            foreach (var app in AndroidApps)
            {
                var start = Utc(date, app.StartHour);

                androidSessions.Add(new AndroidSessionData
                {
                    UserId = userId,
                    DeviceId = AndroidDeviceId,
                    PackageName = app.PackageName,
                    AppLabel = app.AppLabel,
                    SessionStartUtc = start,
                    SessionEndUtc = start.AddMinutes(app.Minutes),
                    // AndroidSyncEndpoint drops a session whose stated duration disagrees with its two
                    // instants by more than two seconds, so the fixture keeps them in step exactly.
                    DurationSeconds = app.Minutes * 60
                });
            }
        }

        dbContext.Set<WebExtensionActivityEntry>().AddRange(webEntries);
        dbContext.Set<DesktopActivityEntry>().AddRange(desktopEntries);
        dbContext.Set<AndroidSessionData>().AddRange(androidSessions);

        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation(
            "Seeded the tracking ledgers: {WebCount} browser windows, {DesktopCount} desktop windows, {AndroidCount} Android sessions.",
            webEntries.Count, desktopEntries.Count, androidSessions.Count);
    }

    // --- notifications ----------------------------------------------------------------------------

    /// <summary>
    /// The bell. Six for the primary account — three unread, three read — across six kinds, so both the
    /// <c>All</c> / <c>Unread</c> filter and the type filter have something to discriminate, plus two for the
    /// secondary account so the bell is per-account rather than global.
    /// </summary>
    /// <remarks>
    /// <para>Payloads are serialized from the typed records in <c>Sydowwe.Framework.Contracts</c> with the
    /// same helper <c>NotificationTextRenderer</c> reads them back with, rather than hand-written JSON: the
    /// renderer falls back to a name-less body for a document it cannot bind, so a drifted literal would seed
    /// rows that render as "Máte novú notifikáciu" and look like a renderer bug.</para>
    /// <para><c>CreatedTimestamp</c> is stamped to <c>UtcNow</c> on insert by the base save, and the bell
    /// orders on it — so it is rewritten in a second pass, from the seed date rather than the clock. Without
    /// that the six rows share one instant and their order is whatever Postgres returns; with the clock they
    /// would differ between two seeds on the same day, which the fixture promises they do not.</para>
    /// </remarks>
    private async Task AddNotificationsAsync(
        long userId, DateOnly today, IReadOnlyDictionary<string, RoutineTimePeriod> periods, CancellationToken ct)
    {
        // Unread first: the stagger below makes each successive row older, and an unread bell that has to be
        // scrolled to is not the state a spec asserting on the Unread filter wants to open.
        var notifications = new List<Notification>
        {
            new()
            {
                Id = NotificationFirst,
                UserId = userId,
                Type = NotificationType.DeadlineApproaching,
                PayloadJson = JsonHelper.Serialize(new DeadlineApproachingPayload("Feature Development")),
                IsRead = false
            },
            new()
            {
                Id = NotificationFirst + 1,
                UserId = userId,
                Type = NotificationType.ReminderDigest,
                PayloadJson = JsonHelper.Serialize(new ReminderDigestPayload(3)),
                IsRead = false
            },
            new()
            {
                Id = NotificationFirst + 2,
                UserId = userId,
                Type = RoutinesNotificationTypes.RoutinePeriodEndingSoon,
                // Weekly holds one item, not yet done this cycle — 1 of 1 remaining, not the 2 of 5 that used
                // to be here and described a group the fixture never had.
                PayloadJson = JsonHelper.Serialize(new RoutinePeriodEndingSoonPayload(
                    periods["Weekly"].Id, "Weekly", Remaining: 1, Total: 1, DaysLeft: 1, Streak: periods["Weekly"].Streak)),
                IsRead = false
            },
            new()
            {
                // The summary the last Daily reset would have sent: read off the period rather than retyped,
                // so it agrees with the newest completion row and the streak AddRoutinePeriodHistory left.
                Id = NotificationFirst + 3,
                UserId = userId,
                Type = RoutinesNotificationTypes.RoutinePeriodEnded,
                PayloadJson = JsonHelper.Serialize(new RoutinePeriodEndedPayload(
                    periods["Daily"].Id, "Daily", CompletedCount: 5, TotalCount: 5, Streak: periods["Daily"].Streak,
                    Outcome: RoutineStreakOutcome.Extended)),
                IsRead = true
            },
            new()
            {
                Id = NotificationFirst + 4,
                UserId = userId,
                Type = HistoryNotificationTypes.TimerBoundary,
                PayloadJson = JsonHelper.Serialize(new TimerBoundaryPayload(
                    TimerBoundaryPhase.Focus, "Bug Fixing", PhaseIndex: 2, PhaseTotal: 4, CycleIndex: 1, CycleTotal: 2)),
                IsRead = true
            },
            new()
            {
                Id = NotificationFirst + 5,
                UserId = userId,
                Type = NotificationType.Test,
                PayloadJson = JsonHelper.Serialize(new TestNotificationPayload("Fixture notification for the e2e suite.")),
                IsRead = true
            },

            // The secondary account's own bell — the isolation check is "sign in as B and see B's rows".
            new()
            {
                Id = SecondaryNotificationFirst,
                UserId = SecondaryUserId,
                Type = NotificationType.ReminderDigest,
                PayloadJson = JsonHelper.Serialize(new ReminderDigestPayload(1)),
                IsRead = false
            },
            new()
            {
                Id = SecondaryNotificationFirst + 1,
                UserId = SecondaryUserId,
                Type = NotificationType.Test,
                PayloadJson = JsonHelper.Serialize(new TestNotificationPayload("Second account only.")),
                IsRead = true
            }
        };

        dbContext.Set<Notification>().AddRange(notifications);
        await dbContext.SaveChangesAsync(ct);

        // Second pass: on a Modified entity only ModifiedTimestamp is re-stamped, so these survive. Six hours
        // apart from 08:00 on the seed date, which spreads them over the last day and a half.
        var newest = Utc(today, 8);

        for (var index = 0; index < notifications.Count; index++)
        {
            var notification = notifications[index];
            notification.CreatedTimestamp = newest.AddHours(-6 * index);
            notification.ReadAt = notification.IsRead ? notification.CreatedTimestamp.AddHours(1) : null;
        }

        await dbContext.SaveChangesAsync(ct);
    }

    // --- the secondary account ------------------------------------------------------------------

    /// <summary>
    /// A second account with data of its own. It exists for the isolation checks — sign in as A, then
    /// as B, and confirm A's per-account <c>localStorage</c> (pinned templates, dismissed hints) did not
    /// follow — so what matters is that every visible string differs from the primary account's.
    /// </summary>
    private async Task SeedSecondaryAccountAsync(DateOnly today, CancellationToken ct)
    {
        const long userId = SecondaryUserId;

        AddActivityRoles(userId, [(SecondaryRoleWork, "Consulting", ColorPalette.Teal, "fas fa-user-tie")]);
        AddActivityCategories(userId, [(SecondaryCategoryGeneral, "General", ColorPalette.Sky)]);

        AddActivity(SecondaryActivityDraftProposal, userId, "Draft Proposal", SecondaryRoleWork, SecondaryCategoryGeneral);
        AddActivity(SecondaryActivityPostParcel, userId, "Post a Parcel", SecondaryRoleWork, SecondaryCategoryGeneral);

        dbContext.TodoLists.Add(new TodoList
        {
            Id = SecondaryListWork,
            UserId = userId,
            Name = "Client Work",
            Text = "Belongs to the second account only",
            Icon = "fas fa-folder-open"
        });

        await dbContext.SaveChangesAsync(ct);

        var priorities = await PriorityIdsAsync(userId, ct);

        dbContext.TodoListItems.AddRange(
            new TodoListItem
            {
                Id = SecondaryItemDraftProposal,
                UserId = userId,
                ActivityId = SecondaryActivityDraftProposal,
                TodoListId = SecondaryListWork,
                TaskPriorityId = priorities["Today"],
                IsDone = false,
                SuggestedTime = FromMinutes(90),
                DueDate = today,
                DisplayOrder = DisplayOrderStart
            },
            new TodoListItem
            {
                Id = SecondaryItemPostParcel,
                UserId = userId,
                ActivityId = SecondaryActivityPostParcel,
                TodoListId = SecondaryListWork,
                TaskPriorityId = priorities["This week"],
                IsDone = false,
                DueDate = today.AddDays(-2),
                DisplayOrder = DisplayOrderStart - DisplayOrderGap
            });

        var start = Utc(today.AddDays(-2), 10);
        dbContext.ActivityHistories.Add(new ActivityHistory
        {
            Id = SecondaryHistoryDraftProposal,
            UserId = userId,
            ActivityId = SecondaryActivityDraftProposal,
            StartTimestamp = start,
            EndTimestamp = start.AddMinutes(105),
            Length = FromMinutes(105)
        });

        await dbContext.SaveChangesAsync(ct);
    }

    // --- reminders ------------------------------------------------------------------------------

    /// <summary>
    /// The reminders surface, in both of the projects that own half of it: the portal's own
    /// <see cref="Reminder"/> rows (what the user asked for) and the module's
    /// <see cref="ReminderDefinition"/> / <see cref="ReminderDispatch"/> rows (scheduler state and the
    /// append-only outcome ledger). Spans both fixture accounts on purpose — the register and its
    /// dashboards roll up <i>every</i> user's reminders, which is what makes them Admin-only and what
    /// distinguishes them from the self-service <c>my-upcoming</c> view.
    ///
    /// <para><b>Why the two sides are written by hand rather than through
    /// <see cref="ReminderRegistrationService"/>.</b> The seam is the right way for the <i>app</i> to
    /// create a reminder, and it is covered by <c>ReminderRegistrationTests</c>. Here it would take the
    /// definition ids away from the fixture — the registry upserts and lets the sequence choose — and a
    /// spec opening <c>/pripomienky/register/:id</c> needs an id it can name. The key format, template key
    /// and payload below are therefore read off that service's constants rather than retyped, so the two
    /// cannot drift apart silently; only the ids are ours.</para>
    ///
    /// <para>The statuses are chosen so no dashboard number is zero and no two are equal: four definitions
    /// with a pending occurrence (three of them inside the overview's 7-day window, one outside it), one
    /// paused, one cancelled. <see cref="AddReminderDispatchesAsync"/> does the same for the outcome
    /// tallies.</para>
    /// </summary>
    private async Task AddRemindersAsync(DateOnly today, CancellationToken ct)
    {
        // The instants the dispatch ledger below is written at. Named here because the definitions have to
        // agree with them: a recurring reminder's LastOccurrenceAt is its newest Sent row, and a definition
        // claiming it last fired at an instant with no ledger row is a state the scanner cannot produce.
        var rentLastSent = Offset(today.AddDays(-1), 8);
        var rentSkipped = Offset(today.AddDays(-3), 8);
        var standupSent = Offset(today.AddDays(-2), 9);
        var standupFailed = Offset(today.AddDays(-4), 9);
        var gymSent = Offset(today.AddDays(-6), 18);
        var gymFailedThenReversed = Offset(today.AddDays(-5), 18);

        var lastOccurrences = new Dictionary<long, DateTimeOffset>
        {
            [ReminderPayRent] = rentLastSent,
            [ReminderStandup] = standupSent,
            [ReminderOldGymClass] = gymSent
        };

        var reminders = new FixtureReminder[]
        {
            new(ReminderCallTheDentist, PrimaryUserId, "Call the dentist", Utc(today.AddDays(2), 15),
                [-30, 0], null, null, ReminderStatus.Active),
            new(ReminderFileTaxReturn, PrimaryUserId, "File the tax return", Utc(today.AddDays(5), 9),
                [0], null, null, ReminderStatus.Active),
            new(ReminderPayRent, PrimaryUserId, "Pay the rent", Utc(today.AddDays(12), 8),
                [0], ReminderRecurrence.Monthly, null, ReminderStatus.Active),

            // Task-linked: RemindAt mirrors the tomorrow-09:00 planner task's instant, exactly as
            // ReminderRegistrationService would have derived it. Not one of today's tasks — a completed or
            // cancelled task retires its reminder, so a reminder on one would be a state the app deletes.
            new(ReminderStandup, PrimaryUserId, "Before the feature block", Utc(today.AddDays(1), 9),
                [-15], null, PlannerTaskFirst + 3, ReminderStatus.Paused),

            new(ReminderOldGymClass, PrimaryUserId, "Gym class", Utc(today.AddDays(-6), 18),
                [0], null, null, ReminderStatus.Cancelled),
            new(SecondaryReminderPostParcel, SecondaryUserId, "Post the parcel", Utc(today.AddDays(1), 11),
                [-60], null, null, ReminderStatus.Active)
        };

        foreach (var reminder in reminders)
        {
            dbContext.Set<Reminder>().Add(new Reminder
            {
                Id = reminder.Id,
                UserId = reminder.UserId,
                Title = reminder.Title,
                RemindAt = reminder.RemindAt,
                LeadOffsetsMinutes = [.. reminder.LeadOffsetsMinutes],
                Recurrence = reminder.Recurrence,
                PlannerTaskId = reminder.PlannerTaskId
            });

            var isRecurring = reminder.Recurrence is not null;

            // The soonest occurrence is the deadline shifted by the earliest (most negative) offset —
            // NormalizeOffsets orders them, and the fixture's arrays are written already ordered.
            var dueAt = new DateTimeOffset(reminder.RemindAt);
            var firstOccurrence = dueAt.AddMinutes(reminder.LeadOffsetsMinutes[0]);

            dbContext.Set<ReminderDefinition>().Add(new ReminderDefinition
            {
                Id = reminder.Id + ReminderDefinitionOffset,

                // The idempotency key, from the one class that owns its format.
                OwnerModule = ReminderRegistrationService.OwnerModule,
                SubjectType = ReminderRegistrationService.SubjectType,
                SubjectId = reminder.Id.ToString(),
                Kind = ReminderRegistrationService.Kind,

                TemplateKey = ReminderRegistrationService.TemplateKey,
                NotificationType = PlanningNotificationTypes.PersonalReminder,
                PayloadJson = JsonHelper.Serialize(
                    new PersonalReminderPayload(reminder.Id, reminder.Title, reminder.PlannerTaskId)),

                ScheduleType = isRecurring ? ReminderScheduleType.RecurringInterval : ReminderScheduleType.OneShot,

                // Per-type fields are meaningful only for the matching ScheduleType; the others stay null
                // rather than being filled with a plausible-looking value nothing reads.
                DueAt = isRecurring ? null : dueAt,
                IntervalPreset = isRecurring ? ToPreset(reminder.Recurrence!.Value) : null,

                // A recurring schedule has no lead-offset concept, so the single offset is folded into the
                // anchor — the same fold BuildSchedule performs.
                AnchorDate = isRecurring ? firstOccurrence : null,

                // BuildSchedule passes the owner's zone for a recurring one; every fixture user is on UTC.
                TimeZoneId = isRecurring ? TimeZoneInfo.Utc.Id : null,
                LeadOffsets = isRecurring
                    ? []
                    : [.. reminder.LeadOffsetsMinutes.Select(offset => new ReminderLeadOffset { OffsetMinutes = offset })],

                // Explicit users, never a resolver: the module's read paths exclude resolver-backed
                // reminders, and a personal reminder has exactly one known recipient anyway.
                RecipientMode = RecipientMode.ExplicitUsers,
                Recipients = [new ReminderRecipient { UserId = reminder.UserId }],

                Status = reminder.Status,

                // Paused and Cancelled both mean "no pending occurrence" — the registry nulls this column on
                // both transitions, and the upcoming reads treat a non-null value as the definition of active.
                NextOccurrenceAt = reminder.Status == ReminderStatus.Active ? firstOccurrence : null,
                LastOccurrenceAt = lastOccurrences.TryGetValue(reminder.Id, out var last) ? last : null
            });
        }

        await dbContext.SaveChangesAsync(ct);

        await AddReminderDispatchesAsync(
            rentLastSent, rentSkipped, standupSent, standupFailed, gymSent, gymFailedThenReversed, ct);
    }

    /// <summary>
    /// The last seven days of the append-only dispatch ledger — what the history grid pages and what the
    /// overview's recent-outcome card tallies. Every outcome appears, and the two <c>Failed</c> rows are not
    /// interchangeable: one stands, the other is superseded by a <c>Reversed</c> row pointing at it. That is
    /// what makes the overview's "needs attention" count 1 rather than 2, and a fixture without the pair
    /// cannot tell a correct implementation from one that simply counts failures.
    /// </summary>
    /// <remarks>
    /// Rows are inserted, never updated — a correction is a new reversal row. The <c>RecipientsSnapshot</c>
    /// carries user ids only, as its PII contract requires.
    /// </remarks>
    private async Task AddReminderDispatchesAsync(
        DateTimeOffset rentLastSent,
        DateTimeOffset rentSkipped,
        DateTimeOffset standupSent,
        DateTimeOffset standupFailed,
        DateTimeOffset gymSent,
        DateTimeOffset gymFailedThenReversed,
        CancellationToken ct)
    {
        var id = ReminderDispatchFirst;

        ReminderDispatch Dispatch(
            long reminderId, DateTimeOffset occurrenceAt, DispatchOutcome outcome,
            SkipReason? skipReason = null, string? failureDetail = null,
            long? reversesDispatchId = null, TimeSpan? delay = null)
        {
            var rowId = id++;

            return new ReminderDispatch
            {
                Id = rowId,
                ReminderDefinitionId = reminderId + ReminderDefinitionOffset,
                OccurrenceAt = occurrenceAt,
                DispatchedAt = occurrenceAt + (delay ?? TimeSpan.Zero),
                Outcome = outcome,
                SkipReason = skipReason,
                FailureDetail = failureDetail,
                NotificationTypeSnapshot = PlanningNotificationTypes.PersonalReminder,
                TemplateKeySnapshot = ReminderRegistrationService.TemplateKey,
                RecipientsSnapshot = JsonHelper.Serialize(new[] { PrimaryUserId }),
                // Derived from the row rather than Activity.Current, so two resets produce the same ledger.
                CorrelationId = $"e2e-dispatch-{rowId}",
                ReversesDispatchId = reversesDispatchId
            };
        }

        var failedThenReversed = Dispatch(
            ReminderOldGymClass, gymFailedThenReversed, DispatchOutcome.Failed,
            // The failure *shape*, never an exception message — this ledger is retained and exported.
            failureDetail: "NotificationChannelUnavailable");

        dbContext.Set<ReminderDispatch>().AddRange(
            Dispatch(ReminderPayRent, rentLastSent, DispatchOutcome.Sent),
            Dispatch(ReminderStandup, standupSent, DispatchOutcome.Sent),
            Dispatch(ReminderOldGymClass, gymSent, DispatchOutcome.Sent),
            Dispatch(ReminderPayRent, rentSkipped, DispatchOutcome.Skipped, skipReason: SkipReason.QuietHours),
            Dispatch(ReminderStandup, standupFailed, DispatchOutcome.Failed,
                failureDetail: "MissingRecipientResolver"),
            failedThenReversed,
            Dispatch(ReminderOldGymClass, gymFailedThenReversed, DispatchOutcome.Reversed,
                reversesDispatchId: failedThenReversed.Id, delay: TimeSpan.FromHours(1)));

        await dbContext.SaveChangesAsync(ct);
    }

    // --- scheduler ------------------------------------------------------------------------------

    /// <summary>
    /// One orphaned job — an <c>Active</c> registry row whose handler key no registered handler serves — and
    /// one run of it from before the handler went away. Without them every orphan surface of the scheduler
    /// views is unreachable: a running host has a handler for every job its registrars wrote, and
    /// <c>RegisterRecurringJobAsync</c> rejects an unknown handler key, so the app cannot produce this state.
    ///
    /// <para><b>An upsert, unlike everything else here.</b> The reset leaves the <c>scheduler</c> schema alone
    /// (see <see cref="TruncateAsync"/>), so these two rows are found by <see cref="E2eFixtureIds.OrphanedJobKey"/>
    /// and <see cref="E2eFixtureIds.OrphanedJobRunCorrelationId"/> and rewritten in place. That keeps their ids
    /// stable across resets without claiming a fixture band in a table whose sequence the registrars and the
    /// dispatcher have been advancing for as long as the database has existed.</para>
    ///
    /// <para><b><c>NextRunAt</c> stays null.</b> Nothing ever gives an orphan a Quartz trigger — the registrar
    /// skips a row whose handler is gone — so a non-null value would be a stale one, and a stale past value
    /// puts the row on the overdue list too and in front of <c>OverdueJobSweep</c>'s alerts on every sweep.
    /// The orphan list is the one this row is for.</para>
    /// </summary>
    private async Task<IReadOnlyDictionary<string, long>> UpsertOrphanedSchedulerJobAsync(DateOnly today, CancellationToken ct)
    {
        var lastRunStartedAt = Utc(today.AddDays(-1), 3);
        var lastRunFinishedAt = lastRunStartedAt.AddSeconds(2);

        var job = await dbContext.ScheduledJobs.IgnoreQueryFilters()
            .FirstOrDefaultAsync(row => row.JobKey == OrphanedJobKey, ct);

        if (job is null)
        {
            job = new ScheduledJob { JobKey = OrphanedJobKey, HandlerKey = OrphanedJobHandlerKey, OwnerModule = "E2eFixture" };
            dbContext.ScheduledJobs.Add(job);
        }

        job.HandlerKey = OrphanedJobHandlerKey;
        job.OwnerModule = "E2eFixture";
        job.Description = "Fixture job whose handler no longer exists.";
        job.ScheduleType = JobScheduleType.Cron;
        job.Cron = "0 0 3 * * ?";
        job.IntervalPreset = null;
        job.IntervalCount = null;
        job.RunAtUtc = null;
        job.TimeZoneId = null;
        job.MisfirePolicy = MisfirePolicy.FireAndProceed;
        job.DisallowConcurrent = false;
        job.MaxRetries = 0;
        job.AlertOnFailure = false;
        job.PayloadJson = null;
        job.Status = JobStatus.Active;
        job.IsActive = true;
        job.NextRunAt = null;
        job.LastRunAt = lastRunStartedAt;
        job.LastOutcome = RunOutcome.Succeeded;
        job.RunningSince = null;

        await dbContext.SaveChangesAsync(ct);

        var run = await dbContext.ScheduledJobRuns
            .FirstOrDefaultAsync(row => row.ScheduledJobId == job.Id && row.CorrelationId == OrphanedJobRunCorrelationId, ct);

        if (run is null)
        {
            run = new ScheduledJobRun
            {
                ScheduledJobId = job.Id,
                JobKeySnapshot = OrphanedJobKey,
                HandlerKeySnapshot = OrphanedJobHandlerKey,
                CorrelationId = OrphanedJobRunCorrelationId
            };
            dbContext.ScheduledJobRuns.Add(run);
        }

        // The run ledger is append-only for the app; this row is the fixture's own, so rewriting it is how it
        // stays the same row. DurationMs is a stored computed column and follows from the two instants.
        run.JobKeySnapshot = OrphanedJobKey;
        run.HandlerKeySnapshot = OrphanedJobHandlerKey;
        run.ScheduledFireTime = lastRunStartedAt;
        run.StartedAt = lastRunStartedAt;
        run.FinishedAt = lastRunFinishedAt;
        run.Outcome = RunOutcome.Succeeded;
        run.ErrorMessage = null;
        run.ErrorType = null;
        run.TriggerSource = TriggerSource.Scheduled;
        run.RetryAttempt = 0;
        run.PayloadSnapshotJson = null;
        run.ReplaysRunId = null;

        await dbContext.SaveChangesAsync(ct);

        return new Dictionary<string, long>
        {
            ["orphanedJob"] = job.Id,
            ["orphanedJobRun"] = run.Id
        };
    }

    /// <summary>
    /// The portal recurrence → module preset mapping, for the fixture's one recurring reminder.
    /// <c>ReminderRegistrationService.ToPreset</c> is the authority; this covers only the values the fixture
    /// uses and throws on anything else rather than guessing.
    /// </summary>
    private static ReminderIntervalPreset ToPreset(ReminderRecurrence recurrence) => recurrence switch
    {
        ReminderRecurrence.Daily => ReminderIntervalPreset.Daily,
        ReminderRecurrence.Weekly => ReminderIntervalPreset.Weekly,
        ReminderRecurrence.Monthly => ReminderIntervalPreset.Monthly,
        _ => throw new ArgumentOutOfRangeException(nameof(recurrence), recurrence,
            "The fixture has no module preset for this recurrence; add it here and in ReminderRegistrationService.")
    };

    // --- shared builders ------------------------------------------------------------------------

    private void AddActivityRoles(long userId, (long Id, string Name, string Color, string Icon)[] roles)
    {
        foreach (var role in roles)
            dbContext.ActivityRoles.Add(new ActivityRole
            {
                Id = role.Id,
                UserId = userId,
                Name = role.Name,
                Color = role.Color,
                Icon = role.Icon
                // SystemKey stays null: the three keyed roles belong to IUserDefaultsService, and a
                // second row carrying a key would violate the filtered (user_id, system_key) index.
            });
    }

    private void AddActivityCategories(long userId, (long Id, string Name, string Color)[] categories)
    {
        foreach (var category in categories)
            dbContext.ActivityCategories.Add(new ActivityCategory
            {
                Id = category.Id,
                UserId = userId,
                Name = category.Name,
                Color = category.Color
            });
    }

    private void AddActivity(long id, long userId, string name, long roleId, long? categoryId, bool isArchived = false) =>
        dbContext.Activities.Add(new Activity
        {
            Id = id,
            UserId = userId,
            Name = name,
            RoleId = roleId,
            CategoryId = categoryId,
            IsUnavoidable = false,
            IsArchived = isArchived
        });

    // --- lookups the fixture does not own -------------------------------------------------------

    /// <summary>
    /// Task priority ids, keyed by the seeded English text. <c>IgnoreQueryFilters</c> is mandatory: the
    /// portal scopes every <c>IEntityWithUser</c> read to the ambient user, and this runs with no
    /// authenticated user at all — the explicit <c>UserId</c> predicate is the scoping.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, long>> PriorityIdsAsync(long userId, CancellationToken ct) =>
        await dbContext.TaskPriorities
            .IgnoreQueryFilters()
            .Where(priority => priority.UserId == userId)
            .ToDictionaryAsync(priority => priority.Text, priority => priority.Id, ct);

    private async Task<IReadOnlyDictionary<string, long>> RoutineTimePeriodIdsAsync(long userId, CancellationToken ct) =>
        await dbContext.RoutineTimePeriods
            .IgnoreQueryFilters()
            .Where(period => period.UserId == userId)
            .ToDictionaryAsync(period => period.Text, period => period.Id, ct);

    /// <summary>
    /// The four periods themselves, tracked — the routine history writes period-level state onto two of
    /// them, and the other two get their freeze window stamped.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, RoutineTimePeriod>> RoutineTimePeriodsAsync(long userId, CancellationToken ct) =>
        await dbContext.RoutineTimePeriods
            .IgnoreQueryFilters()
            .Where(period => period.UserId == userId)
            .ToDictionaryAsync(period => period.Text, ct);

    /// <summary>
    /// Task importance ids keyed by their rank marker rather than their text: <c>Critical</c> and
    /// <c>Optional</c> are the two the seeder needs, and the marker values are constants on the entity while
    /// the text is a label a user can rename.
    /// </summary>
    private async Task<IReadOnlyDictionary<int, long>> ImportanceIdsAsync(long userId, CancellationToken ct) =>
        await dbContext.Set<TaskImportance>()
            .IgnoreQueryFilters()
            .Where(importance => importance.UserId == userId)
            .ToDictionaryAsync(importance => importance.Importance, importance => importance.Id, ct);

    private async Task<LookupIds> LookupIdsAsync(long userId, CancellationToken ct)
    {
        var locationTypes = await dbContext.ActivityLocationTypes.IgnoreQueryFilters()
            .Where(row => row.UserId == userId).ToDictionaryAsync(row => row.Text, row => row.Id, ct);
        var weatherDependencies = await dbContext.ActivityWeatherDependencies.IgnoreQueryFilters()
            .Where(row => row.UserId == userId).ToDictionaryAsync(row => row.Text, row => row.Id, ct);
        var costTiers = await dbContext.ActivityExpectedCostTiers.IgnoreQueryFilters()
            .Where(row => row.UserId == userId).ToDictionaryAsync(row => row.Text, row => row.Id, ct);
        var experienceTypes = await dbContext.ActivityExperienceTypes.IgnoreQueryFilters()
            .Where(row => row.UserId == userId).ToDictionaryAsync(row => row.Text, row => row.Id, ct);

        return new LookupIds(
            locationTypes["Indoor"],
            weatherDependencies["None"],
            costTiers["Free"],
            experienceTypes["Skill"]);
    }

    // --- response -------------------------------------------------------------------------------

    private async Task<TestSeedResponse> BuildResponseAsync(
        DateOnly today, IReadOnlyDictionary<string, long> scheduler, CancellationToken ct)
    {
        var activityCount = await dbContext.Activities.IgnoreQueryFilters()
            .CountAsync(activity => activity.UserId == PrimaryUserId, ct);
        var itemCount = await dbContext.TodoListItems.IgnoreQueryFilters()
            .CountAsync(item => item.UserId == PrimaryUserId, ct);
        var overdueCount = await dbContext.TodoListItems.IgnoreQueryFilters()
            .CountAsync(item => item.UserId == PrimaryUserId && !item.IsDone && item.DueDate != null && item.DueDate < today, ct);
        var historyCount = await dbContext.ActivityHistories.IgnoreQueryFilters()
            .CountAsync(history => history.UserId == PrimaryUserId, ct);
        var plannerCount = await dbContext.PlannerTasks.IgnoreQueryFilters()
            .CountAsync(task => task.UserId == PrimaryUserId, ct);
        var calendarCount = await dbContext.Calendars.IgnoreQueryFilters()
            .CountAsync(calendar => calendar.UserId == PrimaryUserId, ct);

        var templateCount = await dbContext.Set<TaskPlannerDayTemplate>().IgnoreQueryFilters()
            .CountAsync(template => template.UserId == PrimaryUserId, ct);
        var templateTaskCount = await dbContext.Set<TemplatePlannerTask>().IgnoreQueryFilters()
            .CountAsync(task => task.UserId == PrimaryUserId, ct);

        var webEntryCount = await dbContext.Set<WebExtensionActivityEntry>().IgnoreQueryFilters()
            .CountAsync(entry => entry.UserId == PrimaryUserId, ct);
        var desktopEntryCount = await dbContext.Set<DesktopActivityEntry>().IgnoreQueryFilters()
            .CountAsync(entry => entry.UserId == PrimaryUserId, ct);

        // The same GROUP BY FetchTableDistinctDesktopEntry does, so the count reported is the one that grid
        // paginates — not the entry count, which is two orders of magnitude larger.
        var distinctDesktopCount = await dbContext.Set<DesktopActivityEntry>().IgnoreQueryFilters()
            .Where(entry => entry.UserId == PrimaryUserId)
            .GroupBy(entry => new { entry.ProductName, entry.ProcessName, entry.WindowTitle })
            .CountAsync(ct);

        var androidSessionCount = await dbContext.Set<AndroidSessionData>().IgnoreQueryFilters()
            .CountAsync(session => session.UserId == PrimaryUserId, ct);
        var distinctAndroidCount = await dbContext.Set<AndroidSessionData>().IgnoreQueryFilters()
            .Where(session => session.UserId == PrimaryUserId)
            .GroupBy(session => new { session.PackageName, session.AppLabel })
            .CountAsync(ct);

        // Notification carries no IEntityWithUser navigation — its UserId is the recipient, so it has no
        // global filter to ignore and the predicate below is the whole of the scoping.
        var notificationCount = await dbContext.Set<Notification>()
            .CountAsync(notification => notification.UserId == PrimaryUserId, ct);
        var unreadNotificationCount = await dbContext.Set<Notification>()
            .CountAsync(notification => notification.UserId == PrimaryUserId && !notification.IsRead, ct);

        // Reminder counts are deliberately NOT scoped to the primary account: the admin views these describe
        // roll up every user's reminders, so a per-user count would not be the number any of them renders.
        var definitionCount = await dbContext.Set<ReminderDefinition>().CountAsync(ct);
        var upcomingReminderCount = await dbContext.Set<ReminderDefinition>()
            .CountAsync(definition => definition.NextOccurrenceAt != null, ct);
        var dispatchCount = await dbContext.Set<ReminderDispatch>().CountAsync(ct);

        // The same predicate ReminderDashboardQueries.EffectiveFailuresOnly applies — a Failed row no
        // reversal row points at.
        var reversedDispatchIds = dbContext.Set<ReminderDispatch>()
            .Where(dispatch => dispatch.ReversesDispatchId != null)
            .Select(dispatch => dispatch.ReversesDispatchId!.Value);
        var effectiveFailureCount = await dbContext.Set<ReminderDispatch>()
            .CountAsync(dispatch => dispatch.Outcome == DispatchOutcome.Failed
                                    && !reversedDispatchIds.Contains(dispatch.Id), ct);

        // RoutinePeriodCompletion carries no UserId; the scoping runs through its period, and so does the
        // query filter — lifted here like every other count, with the explicit predicate as the scoping.
        var completions = dbContext.Set<RoutinePeriodCompletion>().IgnoreQueryFilters()
            .Where(completion => completion.RoutineTimePeriod.UserId == PrimaryUserId);
        var completionCount = await completions.CountAsync(ct);
        var frozenCompletionCount = await completions.CountAsync(completion => completion.IsFrozen, ct);

        return new TestSeedResponse(
            Fixture: FixtureVersion,
            SeedDateUtc: today,
            PrimaryUser: new TestSeedAccount(PrimaryUserId, PrimaryUserEmail, PrimaryUserPassword, "EN"),
            SecondaryUser: new TestSeedAccount(SecondaryUserId, SecondaryUserEmail, SecondaryUserPassword, "EN"),
            AdminUser: new TestSeedAccount(AdminUserId, AdminUserEmail, AdminUserPassword, "EN"),
            Activities: new Dictionary<string, long>
            {
                ["bugFixing"] = ActivityBugFixing,
                ["featureDevelopment"] = ActivityFeatureDevelopment,
                ["codeReview"] = ActivityCodeReview,
                ["morningExercise"] = ActivityMorningExercise,
                ["groceryShopping"] = ActivityGroceryShopping,
                ["mealPreparation"] = ActivityMealPreparation,
                ["readANovel"] = ActivityReadANovel,
                ["learnBouldering"] = ActivityLearnBouldering,
                ["buildABookshelf"] = ActivityBuildABookshelf,
                ["archived"] = ActivityRetiredHobby,
                ["notDeletable"] = ActivityWriteBlogPost,
                ["deletable"] = ActivityUnusedIdea,
                ["deepCleanKitchen"] = ActivityDeepCleanKitchen
            },
            TodoLists: new Dictionary<string, long>
            {
                ["focus"] = PrimaryListFocus,
                ["errands"] = PrimaryListErrands,
                ["empty"] = PrimaryListSomeday,
                ["secondaryWork"] = SecondaryListWork
            },
            TodoListItems: new Dictionary<string, long>
            {
                // Two distinct items: the second is the only one in the fixture that also carries a
                // step checklist, so a spec asserting on the estimate chip alone is not forced to
                // reason about steps it did not ask for.
                ["withEstimate"] = ItemFeatureDevelopment,
                ["withEstimateAndSteps"] = ItemBugFixing,
                ["multiCountPartial"] = ItemDeepCleanKitchen,
                ["completedToday"] = ItemCodeReviewDone,
                ["temptationBundled"] = ItemBuildABookshelf,
                ["mostOverdue"] = ItemRenewPassport,
                ["leastOverdue"] = ItemReturnLibraryBook
            },
            Profiles: new Dictionary<string, long>
            {
                ["backlog"] = BacklogProfileReadANovel,
                ["bucketList"] = BucketListProfileLearnBouldering,
                ["project"] = ProjectProfileBuildABookshelf,
                ["memoryAnchor"] = MemoryAnchorLearnBouldering,
                ["memoryAnchorOnArchived"] = MemoryAnchorRetiredHobby
            },
            Routines: new Dictionary<string, long>
            {
                ["daily"] = RoutineTakeMedication,
                ["weekly"] = RoutineWeeklyReview,
                ["monthly"] = RoutinePayRent,
                // Keyed by the milestone tier the item's streak clears, which is the SPA's own threshold.
                ["milestone30"] = RoutineDrinkWater,
                ["milestone90"] = RoutineJournal,
                ["milestone180"] = RoutineMakeTheBed,
                ["milestone365"] = RoutineBrushTeeth
            },
            DayPlannerTemplates: new Dictionary<string, long>
            {
                ["homeOffice"] = TemplateHomeOffice,
                ["officeDay"] = TemplateOfficeDay,
                ["pinned"] = TemplateRelaxedWeekend
            },
            TrackingMappings: new Dictionary<string, long>
            {
                ["desktopToActivity"] = DesktopMappingRider,
                ["desktopIgnored"] = DesktopMappingIgnoredLauncher,
                ["androidToActivity"] = AndroidMappingSpotify,
                ["androidIgnored"] = AndroidMappingIgnoredSettings
            },
            Notifications: new Dictionary<string, long>
            {
                ["newestUnread"] = NotificationFirst,
                ["oldestRead"] = NotificationFirst + 5,
                ["secondaryNewest"] = SecondaryNotificationFirst
            },
            Reminders: ReminderIds,
            ReminderDefinitions: ReminderIds.ToDictionary(
                entry => entry.Key, entry => entry.Value + ReminderDefinitionOffset),
            Scheduler: scheduler,
            TaskPriorities: await PriorityIdsAsync(PrimaryUserId, ct),
            RoutineTimePeriods: await RoutineTimePeriodIdsAsync(PrimaryUserId, ct),
            Counts: new TestSeedCounts(
                Activities: activityCount,
                TodoListItems: itemCount,
                OverdueTodoListItems: overdueCount,
                CalibrationTodoListItems: CalibrationSet.Length,
                ActivityHistoryEntries: historyCount,
                PlannerTasks: plannerCount,
                Calendars: calendarCount,
                DayPlannerTemplates: templateCount,
                TemplatePlannerTasks: templateTaskCount,
                WebExtensionEntries: webEntryCount,
                DesktopEntries: desktopEntryCount,
                DistinctDesktopEntries: distinctDesktopCount,
                AndroidSessions: androidSessionCount,
                DistinctAndroidEntries: distinctAndroidCount,
                Notifications: notificationCount,
                UnreadNotifications: unreadNotificationCount,
                ReminderDefinitions: definitionCount,
                UpcomingReminders: upcomingReminderCount,
                ReminderDispatches: dispatchCount,
                ReminderFailuresNeedingAttention: effectiveFailureCount,
                RoutinePeriodCompletions: completionCount,
                FrozenRoutinePeriodCompletions: frozenCompletionCount));
    }

    /// <summary>
    /// The reminder ids by name, shared by both maps in the response — the module-side one is these plus
    /// <see cref="E2eFixtureIds.ReminderDefinitionOffset"/>, so the two can never name different sets.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, long> ReminderIds = new Dictionary<string, long>
    {
        ["callTheDentist"] = ReminderCallTheDentist,
        ["fileTaxReturn"] = ReminderFileTaxReturn,
        ["payRentRecurring"] = ReminderPayRent,
        ["paused"] = ReminderStandup,
        ["cancelled"] = ReminderOldGymClass,
        ["secondaryPostParcel"] = SecondaryReminderPostParcel
    };

    // --- helpers --------------------------------------------------------------------------------

    /// <summary>
    /// <c>MaxItems * DisplayOrderGap</c> from <c>TodoListSettings</c> — the value
    /// <c>GetNextDisplayOrder</c> hands the first item in an empty group. Restated as a constant rather
    /// than injected, because the fixture's whole promise is that the same numbers come back every time
    /// and an appsettings edit must not quietly change one of them.
    /// </summary>
    private const long DisplayOrderStart = 100_000_000;

    private const long DisplayOrderGap = 1_000_000;

    // Fixed rather than Guid.NewGuid(): TodoListStep ids are ValueGeneratedNever and travel to the
    // client, so a spec may address a step directly.
    private static readonly Guid StepBugFixingReproduce = new("e2e57e01-0000-4000-8000-000000000001");
    private static readonly Guid StepBugFixingNullRef = new("e2e57e01-0000-4000-8000-000000000002");
    private static readonly Guid StepBugFixingRegression = new("e2e57e01-0000-4000-8000-000000000003");

    /// <summary>
    /// A UTC instant on a given day. Explicit <see cref="DateTimeKind.Utc"/> because
    /// <c>Npgsql.EnableLegacyTimestampBehavior</c> is off: a <c>DateTime</c> of any other kind is
    /// rejected outright by a <c>timestamptz</c> column.
    /// </summary>
    private static DateTime Utc(DateOnly date, int hour) =>
        new(date.Year, date.Month, date.Day, hour, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The same instant as a <see cref="DateTimeOffset"/> at offset zero — which is where every persisted
    /// <c>DateTimeOffset</c> in this solution has to land, and what building it from a UTC-kind
    /// <see cref="DateTime"/> guarantees.
    /// </summary>
    private static DateTimeOffset Offset(DateOnly date, int hour) => new(Utc(date, hour));

    private static IntTime FromMinutes(int minutes) => new(minutes * 60);

    private sealed record CalibrationEntry(
        long ActivityId,
        long ItemId,
        string Name,
        long RoleId,
        long? CategoryId,
        int EstimateMinutes,
        int[] ActualMinutes);

    private sealed record OverdueEntry(long ActivityId, long ItemId, string Name, long RoleId, int DaysOverdue);

    /// <summary>
    /// One reminder as the fixture declares it — enough to build both the portal row and the module
    /// definition from a single line, so the two cannot disagree about the title, the instant or the offsets.
    /// </summary>
    /// <param name="LeadOffsetsMinutes">
    /// Written already ordered (most negative first), because the soonest occurrence is derived from the
    /// first entry — the ordering <c>NormalizeOffsets</c> performs on the live path.
    /// </param>
    /// <param name="Status">
    /// The module-side lifecycle. The portal row has no status column at all: scheduler state lives in the
    /// module, and mirroring it onto <see cref="Reminder"/> is the drift the entity docs forbid.
    /// </param>
    private sealed record FixtureReminder(
        long Id,
        long UserId,
        string Title,
        DateTime RemindAt,
        int[] LeadOffsetsMinutes,
        ReminderRecurrence? Recurrence,
        long? PlannerTaskId,
        ReminderStatus Status);

    private sealed record WebBlock(
        int StartHour,
        int Minutes,
        string PrimaryDomain,
        string PrimaryUrl,
        string BackgroundDomain);

    /// <param name="IsMapped">
    /// Whether a <see cref="TrackerDesktopMappingByPattern"/> resolves this process to an activity — which
    /// makes its seconds attributable, and is why it is the one block recorded at 60 active seconds.
    /// </param>
    private sealed record DesktopBlock(
        int StartHour,
        int Minutes,
        string ProcessName,
        string ProductName,
        string ExecutablePath,
        string[] WindowTitles,
        int ActiveSeconds,
        bool IsMapped);

    private sealed record AndroidApp(string PackageName, string AppLabel, int StartHour, int Minutes);

    private sealed record LookupIds(
        long LocationTypeIndoor,
        long WeatherDependencyNone,
        long CostTierFree,
        long ExperienceTypeSkill);
}
