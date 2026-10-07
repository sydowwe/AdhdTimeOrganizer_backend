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
        builder.EnumColumn(p => p.SkipReasonKey).HasMaxLength(20);
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

        builder.Property(p => p.AutoCompleteAtEnd).IsRequired();
        builder.Property(p => p.CarryOverCount).IsRequired();
        builder.Property(p => p.PostponeCount).IsRequired();

        // FO6's occurrence link, in the same migration as FO3 phase 2's columns so this hot table is altered once.
        // Navigation-free: the materialiser reads the bare id. SetNull, because deleting a rule must not delete the
        // days it already put on the plan — those are the user's record of what was planned.
        // Deliberately no unique (source_repeating_task_id, calendar_id): moving an occurrence onto a day that
        // already holds one would turn the user's PUT into a 409. RepeatingTaskMaterialiser dedupes instead.
        builder.HasOne<RepeatingPlannerTask>()
            .WithMany()
            .HasForeignKey(p => p.SourceRepeatingTaskId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(p => new { p.UserId, p.CalendarId, p.StartTime });
        // There is deliberately no index on Status alone (PERF-5). Five distinct values and no user prefix, so
        // no read in the slice can use it — every one filters by user and calendar first, which is what the
        // composite above serves. It was pure write amplification.
        //
        // The exception is this partial one, for CompleteFinishedPlannerTasksJobHandler's cross-user scan every
        // five minutes. It holds only the rows that job can act on — auto-closing and InProgress (1, the enum's
        // ordinal) — so it costs nothing on the ordinary write path. Keyed on the flag, not on calendar_id: an
        // index leading with an FK column makes EF drop that FK's own (unfiltered) index as redundant.
        builder.HasIndex(p => p.AutoCompleteAtEnd)
            .HasDatabaseName("ix_planner_task_auto_complete_in_progress")
            .HasFilter("auto_complete_at_end AND status = 1");
    }
}