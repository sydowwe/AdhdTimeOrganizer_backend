using AdhdTimeOrganizer.domain.extServiceContract.user;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.domain.model.entity.timer;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.user;
using AdhdTimeOrganizer.infrastructure.persistence.extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence;

public partial class AppDbContext(DbContextOptions<AppDbContext> options, ILoggedUserService loggedUserService, ILogger<AppDbContext> logger)
    : IdentityDbContext<User, UserRole, long>(options)
{
    public DateOnly CurrentPartitionDate => DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-2);

    public DbSet<Activity> Activities { get; set; }
    public DbSet<Alarm> Alarms { get; set; }
    public DbSet<ActivityCategory> ActivityCategories { get; set; }
    public DbSet<ActivityRole> ActivityRoles { get; set; }
    public DbSet<ActivityHistory> ActivityHistories { get; set; }
    public DbSet<Calendar> Calendars { get; set; }
    public DbSet<TaskImportance> TaskImportances { get; set; }
    public DbSet<PlannerTask> PlannerTasks { get; set; }
    public DbSet<TemplatePlannerTask> TemplatePlannerTasks { get; set; }
    public DbSet<TaskPlannerDayTemplate> TaskPlannerDayTemplates { get; set; }

    public DbSet<RoutineTodoList> RoutineTodoLists { get; set; }
    public DbSet<RoutineTimePeriod> RoutineTimePeriods { get; set; }
    public DbSet<TodoListItem> TodoListItems { get; set; }
    public DbSet<TodoList> TodoLists { get; set; }
    public DbSet<TodoListCategory> TodoListCategories { get; set; }
    public DbSet<TaskPriority> TaskUrgencies { get; set; }
    public DbSet<WebExtensionActivityEntry> WebExtensionActivityEntries { get; set; }
    public DbSet<DesktopActivityEntry> DesktopActivityEntries { get; set; }
    public DbSet<AndroidSessionData> AndroidSessionDataEntries { get; set; }
    public DbSet<TrackerDesktopMappingByPattern> TrackerDesktopMappingByPattern { get; set; }
    public DbSet<TimerPreset> TimerPresets { get; set; }
    public DbSet<PomodoroTimerPreset> PomodoroTimerPresets { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("public");

        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<IdentityUserClaim<long>>(entity => entity.ToTable(name: "user_claim"));
        modelBuilder.Entity<IdentityUserLogin<long>>(entity => entity.ToTable(name: "user_login"));
        modelBuilder.Entity<IdentityUserToken<long>>(entity => entity.ToTable(name: "user_token"));
        modelBuilder.Entity<IdentityUserRole<long>>(entity => entity.ToTable(name: "user__role"));
        modelBuilder.Entity<IdentityRoleClaim<long>>(entity => entity.ToTable(name: "user_role_claim"));


        modelBuilder.ApplyConfigurationsFromAssembly(typeof(UserEntityConfiguration).Assembly);

        modelBuilder.Entity<WebExtensionActivityEntry>()
            .HasQueryFilter(x => x.RecordDate >= CurrentPartitionDate);

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);

    // In your DbContext configuration
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.LogTo(Console.WriteLine, Microsoft.Extensions.Logging.LogLevel.Information);
    }
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        this.BaseSaveChangesAsync();
        this.BaseWithUserEntitySaveChangesAsync(loggedUserService, logger);
        return await base.SaveChangesAsync(cancellationToken);
    }
}