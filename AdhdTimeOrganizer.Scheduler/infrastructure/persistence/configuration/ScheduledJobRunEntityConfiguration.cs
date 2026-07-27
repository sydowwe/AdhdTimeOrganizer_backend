using AdhdTimeOrganizer.Scheduler.domain.entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Scheduler.infrastructure.persistence.configuration;

public class ScheduledJobRunEntityConfiguration : IEntityTypeConfiguration<ScheduledJobRun>
{
    public void Configure(EntityTypeBuilder<ScheduledJobRun> b)
    {
        b.BaseEntityConfigure();

        b.Property(x => x.JobKeySnapshot).HasMaxLength(200).IsRequired();
        b.Property(x => x.HandlerKeySnapshot).HasMaxLength(200).IsRequired();
        b.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
        b.Property(x => x.ErrorType).HasMaxLength(300);
        b.Property(x => x.ErrorMessage).HasColumnType("text");
        b.Property(x => x.PayloadSnapshotJson).HasColumnType("jsonb");

        b.EnumColumn(x => x.Outcome);
        b.EnumColumn(x => x.TriggerSource);

        // DB default 0 backfills pre-existing rows; ValueGeneratedNever keeps EF writing the property (the
        // dispatcher always sets it explicitly, so this is for consistency with MaxRetries — see that config).
        b.Property(x => x.RetryAttempt).HasDefaultValue(0).ValueGeneratedNever();

        // Both timestamps are written at insert, so duration is safe as a stored generated column.
        b.StoredComputedColumn(x => x.DurationMs,
            "(EXTRACT(EPOCH FROM (finished_at - started_at)) * 1000)::integer");

        // Restrict: the run log is an append-only ledger — never cascade-delete history with its job.
        b.HasOne(x => x.ScheduledJob)
            .WithMany()
            .HasForeignKey(x => x.ScheduledJobId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.ReplaysRun)
            .WithMany()
            .HasForeignKey(x => x.ReplaysRunId)
            .OnDelete(DeleteBehavior.Restrict);

        // Powers the phase-03 dedup check ("did we already run this occurrence?") AND DB-enforces it:
        // at most one *actual execution* (Succeeded/Failed) per scheduled occurrence. Partial — a
        // DuplicateFire Skipped row reuses the original's ScheduledFireTime, and off-schedule (manual/replay)
        // fires carry a null ScheduledFireTime; both must stay insertable, so they sit outside the filter.
        // This makes the dispatcher's app-level AnyAsync dedup durable rather than best-effort: a racing
        // second execution of the same occurrence fails here at the DB (caught at the insert → DuplicateFire).
        b.HasIndex(x => new { x.ScheduledJobId, x.ScheduledFireTime })
            .HasDatabaseName("ux_scheduled_job_run_occurrence")
            .IsUnique()
            .HasFilter("outcome IN ('Succeeded', 'Failed') AND scheduled_fire_time IS NOT NULL");
        // Powers the failures view + history sort.
        b.HasIndex(x => new { x.Outcome, x.StartedAt });
    }
}