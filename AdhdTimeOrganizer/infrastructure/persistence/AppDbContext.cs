using AdhdTimeOrganizer.domain.model.entity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.android;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.domain.model.entity.reminder;
using AdhdTimeOrganizer.domain.model.entity.suggestion;
using AdhdTimeOrganizer.Core.domain.model.entity.timer;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.user;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.activityPlanning;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.infrastructure.persistence.configuration.todoList;
using AdhdTimeOrganizer.History.infrastructure.persistence.configuration.activityHistory;
using AdhdTimeOrganizer.Routines.infrastructure.persistence.configuration.todoList;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sydowwe.Framework.domain.audit;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.infrastructure;
using Sydowwe.Framework.infrastructure.persistence;
using Sydowwe.Framework.infrastructure.persistence.configuration;
using Sydowwe.Notifications.domain.entity;
using Sydowwe.Reminders.domain.entity;
using Sydowwe.Scheduler.domain.entity;

namespace AdhdTimeOrganizer.infrastructure.persistence;

public partial class AppDbContext(DbContextOptions<AppDbContext> options, ILoggedUserService loggedUserService, ILogger<AppDbContext> logger)
    : BaseDbContext<User>(options, loggedUserService, logger)
{
    public DateOnly CurrentPartitionDate => DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-2);

    public DbSet<Activity> Activities { get; set; }
    public DbSet<ActivityCategory> ActivityCategories { get; set; }
    public DbSet<ActivityRole> ActivityRoles { get; set; }
    public DbSet<ActivityLocationType> ActivityLocationTypes { get; set; }
    public DbSet<ActivityWeatherDependency> ActivityWeatherDependencies { get; set; }
    public DbSet<ActivityExpectedCostTier> ActivityExpectedCostTiers { get; set; }
    public DbSet<ActivityExperienceType> ActivityExperienceTypes { get; set; }
    public DbSet<ActivityHistory> ActivityHistories { get; set; }
    public DbSet<Calendar> Calendars { get; set; }
    public DbSet<TaskImportance> TaskImportances { get; set; }
    public DbSet<PlannerTask> PlannerTasks { get; set; }
    public DbSet<TemplatePlannerTask> TemplatePlannerTasks { get; set; }
    public DbSet<RepeatingPlannerTask> RepeatingPlannerTasks { get; set; }
    public DbSet<TaskPlannerDayTemplate> TaskPlannerDayTemplates { get; set; }
    public DbSet<RoutineTodoList> RoutineTodoLists { get; set; }
    public DbSet<RoutineTimePeriod> RoutineTimePeriods { get; set; }
    public DbSet<RoutinePeriodCompletion> RoutinePeriodCompletions { get; set; }
    public DbSet<TodoListItem> TodoListItems { get; set; }
    public DbSet<TodoList> TodoLists { get; set; }
    public DbSet<TodoListCategory> TodoListCategories { get; set; }
    public DbSet<TaskPriority> TaskPriorities { get; set; }
    public DbSet<WebExtensionActivityEntry> WebExtensionActivityEntries { get; set; }
    public DbSet<DesktopActivityEntry> DesktopActivityEntries { get; set; }
    public DbSet<AndroidSessionData> AndroidSessionDataEntries { get; set; }
    public DbSet<TrackerDesktopMappingByPattern> TrackerDesktopMappingByPattern { get; set; }
    public DbSet<TrackerAndroidMappingByPattern> TrackerAndroidMappingByPattern { get; set; }
    public DbSet<TimerPreset> TimerPresets { get; set; }
    public DbSet<PomodoroTimerPreset> PomodoroTimerPresets { get; set; }
    public DbSet<UserPlannerSettings> UserPlannerSettings { get; set; }
    public DbSet<PlannerTaskPattern> PlannerTaskPatterns { get; set; }
    public DbSet<ActivityHistoryPattern> ActivityHistoryPatterns { get; set; }
    public DbSet<TemplateSuggestionPattern> TemplateSuggestionPatterns { get; set; }
    public DbSet<Reminder> Reminders { get; set; }

    // --- Notifications module ---
    public DbSet<Notification> Notifications { get; set; }
    public DbSet<NotificationPreference> NotificationPreferences { get; set; }
    public DbSet<NotificationQuietHours> NotificationQuietHours { get; set; }
    public DbSet<PushSubscription> PushSubscriptions { get; set; }

    // --- Reminders module ---
    public DbSet<ReminderDefinition> ReminderDefinitions { get; set; }
    public DbSet<ReminderRecipient> ReminderRecipients { get; set; }
    public DbSet<ReminderDispatch> ReminderDispatches { get; set; }
    public DbSet<ReminderLeadOffset> ReminderLeadOffsets { get; set; }
    public DbSet<ReminderOccurrenceAction> ReminderOccurrenceActions { get; set; }
    public DbSet<ReminderKindPreference> ReminderKindPreferences { get; set; }

    // --- Scheduler module ---
    public DbSet<ScheduledJob> ScheduledJobs { get; set; }
    public DbSet<ScheduledJobRun> ScheduledJobRuns { get; set; }

    /// <summary>
    /// Excluded from the automatic per-user filter because it needs a combined one — see
    /// <see cref="OnModelCreating"/>.
    /// </summary>
    protected override IEnumerable<Type> UserScopingExcludedTypes => [typeof(WebExtensionActivityEntry)];

    /// <summary>
    /// This database has the Identity claim/login tables, so they are mapped rather than ignored
    /// (Framework's default). The other three keep the base's names.
    /// </summary>
    protected override void ConfigureIdentityModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdentityUserClaim<long>>(entity => entity.ToTable("user_claim"));
        modelBuilder.Entity<IdentityUserLogin<long>>(entity => entity.ToTable("user_login"));
        modelBuilder.Entity<IdentityUserToken<long>>(entity => entity.ToTable("user_token"));
        modelBuilder.Entity<IdentityUserRole<long>>(entity => entity.ToTable("user__role"));
        modelBuilder.Entity<IdentityRoleClaim<long>>(entity => entity.ToTable("user_role_claim"));
    }

    /// <summary>
    /// Business audit rows only (explicit IAuditService.LogAsync calls — login, 2FA, password
    /// change). Applied one entity at a time on purpose, instead of Framework's whole-assembly sweep:
    /// the rest of the Framework audit machinery, the partitioned `audit_log` written by
    /// AuditSaveChangesInterceptor, is deliberately NOT wired up here, and mapping it would create a
    /// table nothing writes to — hence the explicit Ignore of the base's AuditLogs set.
    /// </summary>
    protected override void ApplyFrameworkConfigurations(ModelBuilder modelBuilder)
    {
        modelBuilder.Ignore<AuditLog>();
        modelBuilder.ApplyConfiguration(new BusinessAuditLogEntityConfiguration());
    }

    /// <summary>
    /// No-op: <see cref="RefreshTokenConfiguration"/> configures the FK itself, from the principal
    /// end, so that <c>User.RefreshTokens</c> is the navigation rather than a second relationship.
    /// </summary>
    protected override void ConfigureRefreshTokenUserFk(ModelBuilder modelBuilder)
    {
    }

    protected override void ApplyHostConfigurations(ModelBuilder modelBuilder)
    {
        // Module configurations first, then the app's — the app supplies the host-side FKs to User that
        // the modules deliberately leave unconfigured (they don't know the concrete user type).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(Notification).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReminderDefinition).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ScheduledJob).Assembly);

        // AdhdTimeOrganizer.Core (User, Activity + its lookups/profiles/anchors, the timer presets) and
        // then the host's own. Two calls, not one: UserEntityConfiguration moved to Core, so the single
        // typeof(...).Assembly that used to cover everything now covers only Core — the host's remaining
        // configurations would silently drop out of the model, taking their tables with them.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(UserEntityConfiguration).Assembly);
        // AdhdTimeOrganizer.TodoLists (TodoList, TodoListItem, TodoListCategory, TaskPriority) — one
        // call per slice project, for the same reason. Drop it and those four tables vanish from the
        // model while the routine tables that FK into them remain, which fails at model-build time.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TodoListConfiguration).Assembly);
        // AdhdTimeOrganizer.History (ActivityHistory). The three tracking configurations that still sit
        // in the host's configuration/activityHistory/ folder — Desktop, WebExtension, AndroidSessionData
        // — belong to Tracking, not History, and are covered by the host call below.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ActivityHistoryConfiguration).Assembly);
        // AdhdTimeOrganizer.Routines (RoutineTodoList, RoutineTimePeriod, RoutinePeriodCompletion).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RoutineTimePeriodConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlannerTaskConfiguration).Assembly);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Schema, Identity mapping, framework configurations, this context's own configurations and
        // the per-user query filters all come from BaseDbContext, in that order.
        base.OnModelCreating(modelBuilder);

        // WebExtensionActivityEntry needs both the partition date filter and the user filter, so it
        // is excluded from the automatic one above and gets the combined filter here. The user half
        // must respect UserScopingOptions.Enabled the same way ApplyUserQueryFilters does for every
        // other IEntityWithUser, or this entity stays filtered when a deployment turns scoping off.
        var appServices = options.FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider;
        var scopingEnabled = appServices?.GetService<IOptions<UserScopingOptions>>()?.Value?.Enabled ?? false;

        if (scopingEnabled && loggedUserService != null)
            modelBuilder.Entity<WebExtensionActivityEntry>()
                .HasQueryFilter(x => x.RecordDate >= CurrentPartitionDate &&
                                     (!loggedUserService.IsAuthenticated || x.UserId == loggedUserService.GetUserId));
        else
            modelBuilder.Entity<WebExtensionActivityEntry>()
                .HasQueryFilter(x => x.RecordDate >= CurrentPartitionDate);

        OnAppModelCreatingPartial(modelBuilder);
    }

    partial void OnAppModelCreatingPartial(ModelBuilder modelBuilder);
}