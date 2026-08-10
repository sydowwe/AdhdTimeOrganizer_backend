using AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.extensions;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Planning.infrastructure.persistence.configuration.activityPlanning;

public class PlannerTaskConfiguration : IEntityTypeConfiguration<PlannerTask>
{
    public void Configure(EntityTypeBuilder<PlannerTask> builder)
    {
        builder.BaseEntityConfigure();

        builder.IsManyWithOneUser();
        builder.IsManyWithOneActivity();

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

        // PlannerTask.TodolistItemId is deliberately NOT configured here. It is a real FK to
        // TodoLists' todo_list_item with ON DELETE SET NULL, but TodoListItem lives in
        // AdhdTimeOrganizer.TodoLists and this slice does not reference that project — declaring the
        // relationship would be the only thing forcing the reference. It is declared instead in
        // AppDbContext.ConfigureCrossSliceRelationships, where both entity types are visible.
        // Nothing about the column or the constraint changes; only where the model says so.

        builder.HasIndex(p => new { p.UserId, p.CalendarId, p.StartTime });
        builder.HasIndex(p => p.Status);
    }
}