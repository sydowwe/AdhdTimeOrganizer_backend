using AdhdTimeOrganizer.Command.application.@interface.users;
using AdhdTimeOrganizer.Command.domain.model.entity.activity;
using AdhdTimeOrganizer.Command.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.Command.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Command.domain.model.entity.user;
using AdhdTimeOrganizer.Command.infrastructure.persistence.configuration;
using AdhdTimeOrganizer.Command.infrastructure.persistence.configuration.activityHistory;
using AdhdTimeOrganizer.Command.infrastructure.persistence.configuration.activityPlanning;
using AdhdTimeOrganizer.Common.infrastructure.persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AdhdTimeOrganizer.Command.infrastructure.persistence;

public class AppCommandDbContext(DbContextOptions<AppCommandDbContext> options, ILoggedUserService loggedUserService, ILogger<AppCommandDbContext> logger)
    : IdentityDbContext<UserEntity, IdentityRole<long>, long>(options)
{
    public DbSet<Activity> Activities { get; set; }
    public DbSet<Alarm> Alarms { get; set; }
    public DbSet<Category> Categories { get; set; }
    public DbSet<Role> Role { get; set; }
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

        modelBuilder.ApplyConfiguration(new ActivityConfiguration());
        modelBuilder.ApplyConfiguration(new CategoryConfiguration());
        modelBuilder.ApplyConfiguration(new RoleConfiguration());

        modelBuilder.ApplyConfiguration(new AlarmConfiguration());
        modelBuilder.ApplyConfiguration(new ActivityHistoryConfiguration());
        modelBuilder.ApplyConfiguration(new WebExtensionDataConfiguration());

        modelBuilder.ApplyConfiguration(new PlannerTaskConfiguration());
        modelBuilder.ApplyConfiguration(new RoutineToDoListConfiguration());
        modelBuilder.ApplyConfiguration(new ToDoListConfiguration());
        modelBuilder.ApplyConfiguration(new RoutineTimePeriodConfiguration());
        modelBuilder.ApplyConfiguration(new TaskUrgencyConfiguration());

        modelBuilder.ApplyConfiguration(new UserEntityConfiguration());
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        this.BaseSaveChangesAsync();
        this.BaseWithUserEntitySaveChangesAsync(loggedUserService, logger);
        return await base.SaveChangesAsync(cancellationToken);
    }
}