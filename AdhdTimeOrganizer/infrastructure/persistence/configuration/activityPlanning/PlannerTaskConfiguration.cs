using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityPlanning;

public class PlannerTaskConfiguration : IEntityTypeConfiguration<PlannerTask>
{
    public void Configure(EntityTypeBuilder<PlannerTask> builder)
    {
        builder.BaseEntityConfigure();

        builder.IsManyWithOneUser(u => u.PlannerTaskList);
        builder.IsManyWithOneActivity(a => a.PlannerTaskList);

        builder.Property(p => p.StartTime).IsRequired();
        builder.Property(p => p.EndTime).IsRequired();
        builder.Property(p => p.IsBackground).IsRequired();
        builder.Property(p => p.Status).IsRequired();

        builder.Property(p => p.Location).HasMaxLength(200);
        builder.Property(p => p.Notes).HasMaxLength(1000);
        builder.Property(p => p.SkipReason).HasMaxLength(500);
        builder.Property(p => p.GoogleEventId).HasMaxLength(200);

        builder.HasOne(p => p.Calendar)
            .WithMany(c => c.Tasks)
            .HasForeignKey(p => p.CalendarId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Importance)
            .WithMany()
            .HasForeignKey(p => p.ImportanceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.TodolistItem)
            .WithMany()
            .HasForeignKey(p => p.TodolistItemId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(p => new { p.UserId, p.CalendarId, p.StartTime });
        builder.HasIndex(p => p.Status);
    }
}