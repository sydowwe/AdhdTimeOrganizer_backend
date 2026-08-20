using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.infrastructure.persistence.configuration;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.timer;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.user;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.History.infrastructure.persistence.configuration.activityHistory;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.entity.reminder;
using AdhdTimeOrganizer.Planning.domain.model.entity.suggestion;
using AdhdTimeOrganizer.Planning.infrastructure.persistence.configuration.activityPlanning;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.infrastructure.persistence.configuration.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.infrastructure.persistence.configuration.todoList;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.android;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.Tracking.infrastructure.persistence.configuration.activityTracking.desktop;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
    public DbSet<UserRoutineSettings> UserRoutineSettings { get; set; }
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
    public DbSet<PlannerSuggestionFromPlannerTask> PlannerSuggestionsFromPlannerTask { get; set; }
    public DbSet<PlannerSuggestionFromActivityHistory> PlannerSuggestionsFromActivityHistory { get; set; }
    public DbSet<PlannerSuggestionFromDayTemplate> PlannerSuggestionsFromDayTemplate { get; set; }
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
        // AdhdTimeOrganizer.History (ActivityHistory).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ActivityHistoryConfiguration).Assembly);
        // AdhdTimeOrganizer.Routines (RoutineTodoList, RoutineTimePeriod, RoutinePeriodCompletion).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RoutineTimePeriodConfiguration).Assembly);
        // AdhdTimeOrganizer.Planning (Calendar, the four planner-task types, TaskImportance,
        // TaskPlannerDayTemplate, UserPlannerSettings, Reminder, and the three suggestion-pattern
        // views). Reminders are part of this slice — there is no AdhdTimeOrganizer.Reminders.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlannerTaskConfiguration).Assembly);
        // AdhdTimeOrganizer.Tracking (DesktopActivityEntry, WebExtensionActivityEntry, AndroidSessionData
        // and the two Tracker*MappingByPattern lookups). Three of these five configurations used to sit
        // in the host's configuration/activityHistory/ folder despite being Tracking's — the folder
        // structure lied, and they moved with their entities rather than with the folder. Two of the
        // tables are partitioned (IsPartitionedByRange); the generator that emits the partition DDL is
        // wired host-side in Program.cs and AppCommandDbContextFactory and stays there.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DesktopActivityEntryConfiguration).Assembly);
        // AdhdTimeOrganizer.ActivityProfiles (the three Activity*Profile rows, the four per-user
        // activity lookups they FK into, and MemoryAnchor). Drop this call and all eight tables vanish
        // from the model — and because nothing else FKs into them, the model still builds cleanly and
        // the next `migrations add` emits eight DROP TABLEs.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ActivityBacklogProfileConfiguration).Assembly);

        // Anchored on AppDbContext, which cannot move slices. Do not re-anchor this on a configuration
        // type: the Planning extraction moved the type this used to name, silently turning the host's
        // own scan into a second Planning scan and dropping every remaining host configuration from the
        // model — no build error, and the next `migrations add` emitted ~500 lines of table renames.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Last, because it needs entity types from two slices that cannot see each other.
        ConfigureCrossSliceRelationships(modelBuilder);
    }

    /// <summary>
    /// Registers the per-module schema sweep. Every table configured anywhere above is mapped to the
    /// default schema; <see cref="SchemaPerModuleConvention"/> moves each module's into its own, from
    /// the map in <see cref="ModuleSchemas"/>.
    /// <para>
    /// It is a convention and not a line at the end of <see cref="OnModelCreating"/> deliberately —
    /// see the type's remarks. The short version: the model is only trustworthy once EF has finalized
    /// it, and this way no future reordering of the <c>ApplyConfigurationsFromAssembly</c> calls above
    /// can change which schema a table lands in.
    /// </para>
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder.Conventions.Add(_ => new SchemaPerModuleConvention(ModuleSchemas.Resolve));
    }

    /// <summary>
    /// Relationships whose two ends live in slice projects that do not reference one another. The host
    /// is the only place both types are in scope, and it owns the schema anyway, so the FK is declared
    /// here rather than forcing a project reference purely to name the principal.
    /// </summary>
    /// <remarks>
    /// Three today. The first: <c>PlannerTask.TodolistItemId</c> → <c>TodoListItem</c>. Planning
    /// keeps the id column; the navigation property was deleted, because nothing read it — every call
    /// site passes the bare id — and it was the only thing that would have forced a
    /// Planning → TodoLists project reference. Configured with no navigation on either end, which is
    /// what <c>HasOne&lt;T&gt;()</c> / <c>WithMany()</c> without lambdas means; column name, nullability
    /// and <c>SetNull</c> are all unchanged, so this produces no migration.
    /// <para>
    /// The other two are <c>ActivityHistory.TodoListItemId</c> → <c>TodoListItem</c> and
    /// <c>ActivityHistory.RoutineTodoListId</c> → <c>RoutineToDoList</c>: which task a recording was
    /// saved from, stamped when the user accepts the save-to-history prompt on completing one. They
    /// are what makes the daily recap's per-item time exact instead of inferred from the shared
    /// activity. Same navigation-free shape and the same reasoning — History can see neither
    /// TodoLists nor Routines.
    /// </para>
    /// </remarks>
    private static void ConfigureCrossSliceRelationships(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlannerTask>()
            .HasOne<TodoListItem>()
            .WithMany()
            .HasForeignKey(p => p.TodolistItemId)
            .OnDelete(DeleteBehavior.SetNull)
            // Pinned, because the generated name depends on whether TodoListItem's ToTable has run yet
            // when this FK is named. While every configuration lived in one assembly, "PlannerTask"
            // sorted ahead of "ToDoListItem", so the name fell back to the entity-set name
            // ("todo_list_items", plural). Applying the TodoLists slice assembly first flips that to the
            // real table name and silently emits a constraint rename. Naming it here makes the FK
            // independent of assembly order, which will keep shifting as further slices come out.
            .HasConstraintName("fk_planner_task_todo_list_items_todolist_item_id");

        // SetNull, not Cascade, on both: ActivityHistory is the source of truth for recorded time, and
        // deleting a task must not delete the record that you spent that time. The row survives with
        // its link cleared — it simply stops being attributable to an item, which is the same state
        // every recording made before this column existed is already in.
        modelBuilder.Entity<ActivityHistory>()
            .HasOne<TodoListItem>()
            .WithMany()
            .HasForeignKey(h => h.TodoListItemId)
            .OnDelete(DeleteBehavior.SetNull)
            // Pinned for the same reason as the FK above — the derived name depends on the order the
            // ApplyConfigurationsFromAssembly calls run in, which shifts whenever a slice moves.
            .HasConstraintName("fk_activity_history_todo_list_item_todo_list_item_id");

        modelBuilder.Entity<ActivityHistory>()
            .HasOne<RoutineTodoList>()
            .WithMany()
            .HasForeignKey(h => h.RoutineTodoListId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("fk_activity_history_routine_todo_list_routine_todo_list_id");
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