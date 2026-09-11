using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.application.dto.testSupport;
using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.infrastructure.security;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.android;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.domain.serviceContract;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface.manager;
using Sydowwe.Notifications.domain.entity;
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
    /// The three tracking ledgers are deliberately absent: they hold thousands of rows no spec addresses by
    /// id, so they keep their generated ids and their sequences need no nudge.
    /// </remarks>
    private static readonly Type[] ExplicitIdEntities =
    [
        typeof(User), typeof(ActivityRole), typeof(ActivityCategory), typeof(Activity),
        typeof(TodoList), typeof(TodoListItem), typeof(ActivityHistory), typeof(PlannerTask),
        typeof(ActivityBacklogProfile), typeof(ActivityBucketListProfile), typeof(ActivityProjectProfile),
        typeof(MemoryAnchor), typeof(RoutineTodoList),
        typeof(TaskPlannerDayTemplate), typeof(TemplatePlannerTask),
        typeof(TrackerDesktopMappingByPattern), typeof(TrackerAndroidMappingByPattern),
        typeof(Notification)
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

        await CreateAccountAsync(PrimaryUserId, PrimaryUserEmail, PrimaryUserPassword, ct);
        await CreateAccountAsync(SecondaryUserId, SecondaryUserEmail, SecondaryUserPassword, ct);

        await SeedPrimaryAccountAsync(today, ct);
        await SeedSecondaryAccountAsync(today, ct);

        await AdvanceSequencesAsync(ct);

        var response = await BuildResponseAsync(today, ct);

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

        logger.LogInformation("Truncated {TableCount} tables.", targets.Count);
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
    private async Task CreateAccountAsync(long id, string email, string password, CancellationToken ct)
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
            HasExtensionAccess = true,
            FirstDayOfWeek = 1
        };
        user.PasswordHash = passwordHasher.HashPassword(user, password);

        dbContext.Users.Add(user);

        // Every endpoint without explicit roles gets Roles("User","Admin","Root") from the global
        // configurator in Program.cs, so an account with no role row authenticates and then 403s on
        // everything it touches.
        var userRoleId = await dbContext.Roles
            .Where(role => role.NormalizedName == PortalRoleCatalog.User.ToUpperInvariant())
            .Select(role => role.Id)
            .FirstAsync(ct);

        dbContext.UserRoles.Add(new IdentityUserRole<long> { UserId = id, RoleId = userRoleId });

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

        var periods = await RoutineTimePeriodIdsAsync(userId, ct);

        AddRoutines(userId, today, periods);
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
    }

    /// <summary>
    /// Three routines, one per period, with streaks in three different states — running, just started,
    /// and never completed.
    /// </summary>
    private void AddRoutines(long userId, DateOnly today, IReadOnlyDictionary<string, long> periods)
    {
        var never = Utc(today.AddYears(-1), 0);

        dbContext.RoutineTodoLists.AddRange(
            new RoutineTodoList
            {
                Id = RoutineTakeMedication,
                UserId = userId,
                ActivityId = ActivityTakeMedication,
                TimePeriodId = periods["Daily"],
                IsDone = true,
                DisplayOrder = DisplayOrderStart,
                Streak = 12,
                BestStreak = 30,
                LastResetDate = today,
                LastCompletedAt = Utc(today, 8),
                SuggestedDays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday]
            },
            new RoutineTodoList
            {
                Id = RoutineWeeklyReview,
                UserId = userId,
                ActivityId = ActivityWeeklyReview,
                TimePeriodId = periods["Weekly"],
                IsDone = false,
                DisplayOrder = DisplayOrderStart,
                Streak = 1,
                BestStreak = 4,
                LastResetDate = today.AddDays(-2),
                LastCompletedAt = Utc(today.AddDays(-9), 18),
                SuggestedDays = [DayOfWeek.Sunday]
            },
            new RoutineTodoList
            {
                Id = RoutinePayRent,
                UserId = userId,
                ActivityId = ActivityPayRent,
                TimePeriodId = periods["Monthly"],
                IsDone = false,
                DisplayOrder = DisplayOrderStart,
                Streak = 0,
                BestStreak = 0,
                LastCompletedAt = never,
                SuggestedDayOfMonth = 1
            });
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
        long userId, DateOnly today, IReadOnlyDictionary<string, long> periods, CancellationToken ct)
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
                Type = NotificationType.RoutinePeriodEndingSoon,
                PayloadJson = JsonHelper.Serialize(new RoutinePeriodEndingSoonPayload(
                    periods["Weekly"], "Weekly", Remaining: 2, Total: 5, DaysLeft: 1, Streak: 1)),
                IsRead = false
            },
            new()
            {
                Id = NotificationFirst + 3,
                UserId = userId,
                Type = NotificationType.RoutinePeriodEnded,
                PayloadJson = JsonHelper.Serialize(new RoutinePeriodEndedPayload(
                    periods["Daily"], "Daily", CompletedCount: 4, TotalCount: 4, Streak: 12,
                    Outcome: RoutineStreakOutcome.Extended)),
                IsRead = true
            },
            new()
            {
                Id = NotificationFirst + 4,
                UserId = userId,
                Type = NotificationType.TimerBoundary,
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

    private async Task<TestSeedResponse> BuildResponseAsync(DateOnly today, CancellationToken ct)
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

        return new TestSeedResponse(
            Fixture: FixtureVersion,
            SeedDateUtc: today,
            PrimaryUser: new TestSeedAccount(PrimaryUserId, PrimaryUserEmail, PrimaryUserPassword, "EN"),
            SecondaryUser: new TestSeedAccount(SecondaryUserId, SecondaryUserEmail, SecondaryUserPassword, "EN"),
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
                ["memoryAnchor"] = MemoryAnchorLearnBouldering
            },
            Routines: new Dictionary<string, long>
            {
                ["daily"] = RoutineTakeMedication,
                ["weekly"] = RoutineWeeklyReview,
                ["monthly"] = RoutinePayRent
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
                UnreadNotifications: unreadNotificationCount));
    }

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
