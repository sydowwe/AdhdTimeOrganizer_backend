using AdhdTimeOrganizer.domain.model.entity;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.android;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.domain.model.entity.suggestion;
using AdhdTimeOrganizer.domain.model.entity.timer;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.user;
using AdhdTimeOrganizer.Notifications.domain.entity;
using AdhdTimeOrganizer.Reminders.domain.entity;
using AdhdTimeOrganizer.Scheduler.domain.entity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.infrastructure.persistence;
using Sydowwe.Framework.infrastructure.persistence.configuration;
using RefreshToken = Sydowwe.Framework.domain.entity.user.RefreshToken;

namespace AdhdTimeOrganizer.infrastructure.persistence;

public partial class AppDbContext(DbContextOptions<AppDbContext> options, ILoggedUserService loggedUserService, ILogger<AppDbContext> logger)
    : IdentityDbContext<User, UserRole, long>(options)
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
    public DbSet<RefreshToken> RefreshTokens { get; set; }
    public DbSet<UserPlannerSettings> UserPlannerSettings { get; set; }
    public DbSet<PlannerTaskPattern> PlannerTaskPatterns { get; set; }
    public DbSet<ActivityHistoryPattern> ActivityHistoryPatterns { get; set; }
    public DbSet<TemplateSuggestionPattern> TemplateSuggestionPatterns { get; set; }

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("public");

        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<IdentityUserClaim<long>>(entity => entity.ToTable("user_claim"));
        modelBuilder.Entity<IdentityUserLogin<long>>(entity => entity.ToTable("user_login"));
        modelBuilder.Entity<IdentityUserToken<long>>(entity => entity.ToTable("user_token"));
        modelBuilder.Entity<IdentityUserRole<long>>(entity => entity.ToTable("user__role"));
        modelBuilder.Entity<IdentityRoleClaim<long>>(entity => entity.ToTable("user_role_claim"));


        // Module configurations first, then the app's — the app supplies the host-side FKs to User that
        // the modules deliberately leave unconfigured (they don't know the concrete user type).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(Notification).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReminderDefinition).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ScheduledJob).Assembly);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(UserEntityConfiguration).Assembly);

        // Business audit rows (explicit IAuditService.LogAsync calls — login, 2FA, password change).
        // Applied one entity at a time on purpose: the rest of the Framework audit machinery, the
        // partitioned `audit_log` written by AuditSaveChangesInterceptor, is deliberately NOT wired up
        // here, and mapping it would create a table nothing writes to.
        modelBuilder.ApplyConfiguration(new BusinessAuditLogEntityConfiguration());

        // Apply user-scoped filter to all IEntityWithUser entities except WebExtensionActivityEntry
        // which needs a combined filter below.
        modelBuilder.ApplyUserQueryFilters(loggedUserService, [typeof(WebExtensionActivityEntry)]);

        // WebExtensionActivityEntry needs both the partition date filter and the user filter.
        if (loggedUserService != null)
            modelBuilder.Entity<WebExtensionActivityEntry>()
                .HasQueryFilter(x => x.RecordDate >= CurrentPartitionDate &&
                                     (!loggedUserService.IsAuthenticated || x.UserId == loggedUserService.GetUserId));
        else
            modelBuilder.Entity<WebExtensionActivityEntry>()
                .HasQueryFilter(x => x.RecordDate >= CurrentPartitionDate);

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);

    // In your DbContext configuration
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.LogTo(Console.WriteLine, LogLevel.Information);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        this.BaseSaveChangesAsync();
        this.BaseWithUserEntitySaveChangesAsync(loggedUserService, logger);
        return await base.SaveChangesAsync(cancellationToken);
    }
}