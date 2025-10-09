using AdhdTimeOrganizer.domain.extServiceContract.user;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.user;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.persistence;

public partial class AppCommandDbContext(DbContextOptions<AppCommandDbContext> options, ILoggedUserService loggedUserService, ILogger<AppCommandDbContext> logger)
    : IdentityDbContext<User, UserRole, long>(options)
{
    public DbSet<Activity> Activities { get; set; }
    public DbSet<Alarm> Alarms { get; set; }
    public DbSet<ActivityCategory> ActivityCategories { get; set; }
    public DbSet<ActivityRole> ActivityRoles { get; set; }
    public DbSet<ActivityHistory> ActivityHistories { get; set; }
    public DbSet<PlannerTask> PlannerTasks { get; set; }
    public DbSet<RoutineTodoList> RoutineTodoLists { get; set; }
    public DbSet<RoutineTimePeriod> RoutineTimePeriods { get; set; }
    public DbSet<TodoList> TodoLists { get; set; }
    public DbSet<TaskPriority> TaskUrgencies { get; set; }
    public DbSet<WebExtensionData> WebExtensionData { get; set; }

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