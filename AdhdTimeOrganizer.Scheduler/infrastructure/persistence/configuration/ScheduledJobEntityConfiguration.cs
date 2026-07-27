using AdhdTimeOrganizer.Scheduler.domain.entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Scheduler.infrastructure.persistence.configuration;

public class ScheduledJobEntityConfiguration : IEntityTypeConfiguration<ScheduledJob>
{
    public void Configure(EntityTypeBuilder<ScheduledJob> b)
    {
        b.BaseEntityConfigure();

        b.Property(x => x.JobKey).HasMaxLength(200).IsRequired();
        b.Property(x => x.HandlerKey).HasMaxLength(200).IsRequired();
        b.Property(x => x.OwnerModule).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.Cron).HasMaxLength(120);
        b.Property(x => x.TimeZoneId).HasMaxLength(64);

        b.EnumColumn(x => x.ScheduleType);
        b.EnumColumn(x => x.IntervalPreset);
        b.EnumColumn(x => x.MisfirePolicy);
        b.EnumColumn(x => x.Status);
        b.EnumColumn(x => x.LastOutcome);

        // DB default 3 backfills pre-existing rows on the column-add; ValueGeneratedNever makes EF ALWAYS
        // write the property (without it, HasDefaultValue treats the CLR default 0 as "unset" and the DB fills
        // in 3 — so a job that means MaxRetries = 0 to DISABLE retries could never persist that 0).
        b.Property(x => x.MaxRetries).HasDefaultValue(3).ValueGeneratedNever();

        // DB default true backfills pre-existing rows on the column-add (alerting is ON by default).
        // ValueGeneratedNever forces EF to ALWAYS write the property: without it, HasDefaultValue treats the
        // CLR default (false) as "unset" and the DB fills in true — so a job that opts OUT (AlertOnFailure =
        // false) could never persist that false. Same trap as MaxRetries, just inverted.
        b.Property(x => x.AlertOnFailure).HasDefaultValue(true).ValueGeneratedNever();

        b.Property(x => x.PayloadJson).HasColumnType("jsonb");

        // The idempotency-key contract guarantee — enforced by the DB, not a code convention.
        b.HasIndex(x => x.JobKey).IsUnique();
    }
}