using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityPlanning;

public class PlannerTaskConfiguration : IEntityTypeConfiguration<PlannerTask>
{
    public void Configure(EntityTypeBuilder<PlannerTask> builder)
    {
        builder.BaseEntityConfigure();

        builder.Property(p => p.IsDone).HasDefaultValue(false).IsRequired();
        builder.IsManyWithOneUser(u => u.PlannerTaskList);
        builder.IsManyWithOneActivity(a=>a.PlannerTaskList);

        builder.Property(p => p.StartTimestamp).IsRequired();
        builder.Property(p => p.MinuteLength).IsRequired();
        builder.Property(p => p.Color).HasMaxLength(7);

        builder.HasIndex(p => new { userId = p.UserId, startTimestamp = p.StartTimestamp })
            .IsUnique();
    }
}