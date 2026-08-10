using AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.extensions;
using AdhdTimeOrganizer.Planning.domain.model.entity.reminder;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Planning.infrastructure.persistence.configuration.reminder;

public class ReminderConfiguration : IEntityTypeConfiguration<Reminder>
{
    public void Configure(EntityTypeBuilder<Reminder> builder)
    {
        builder.BaseEntityConfigure();

        builder.IsManyWithOneUser();

        builder.Property(r => r.Title).IsRequired().HasMaxLength(200);
        builder.Property(r => r.Note).HasMaxLength(1000);
        builder.Property(r => r.RemindAt).IsRequired();

        // Same shape as UserPlannerSettings.PredefinedSkipReasons: a short primitive list nothing queries
        // element-wise, so jsonb beats a child table.
        builder.Property(r => r.LeadOffsetsMinutes)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.EnumColumn(r => r.Recurrence);

        // Cascade is the point of using a real FK here (see Reminder.PlannerTaskId): deleting a task takes
        // its reminders with it, so no planner-task delete path can leak an orphan by forgetting to.
        // The module-side ReminderDefinition is NOT reached by this cascade — the delete endpoints cancel it
        // explicitly through the registry, which is why they read the reminder ids before deleting the task.
        builder.HasOne(r => r.PlannerTask)
            .WithMany()
            .HasForeignKey(r => r.PlannerTaskId)
            .OnDelete(DeleteBehavior.Cascade);

        // The day view's query: this user's reminders inside a local-day instant range.
        builder.HasIndex(r => new { r.UserId, r.RemindAt });
        // The planner-task sync path resolves "which reminders does this task have?" on every status/time change.
        builder.HasIndex(r => r.PlannerTaskId);
    }
}