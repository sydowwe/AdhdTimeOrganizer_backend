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
    public DbSet<RoutineToDoList> RoutineToDoLists { get; set; }
    public DbSet<RoutineTimePeriod> RoutineTimePeriods { get; set; }
    public DbSet<ToDoList> ToDoLists { get; set; }
    public DbSet<TaskUrgency> TaskUrgencies { get; set; }
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


        modelBuilder.HasSequence<int>("user_personal_number_seq")
            .StartsAt(1000)
            .IncrementsBy(11);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(UserEntityConfiguration).Assembly);

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        this.BaseSaveChangesAsync();
        this.BaseWithUserEntitySaveChangesAsync(loggedUserService, logger);
        return await base.SaveChangesAsync(cancellationToken);
    }
}