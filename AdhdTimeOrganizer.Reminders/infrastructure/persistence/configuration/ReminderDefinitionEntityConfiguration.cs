using AdhdTimeOrganizer.Reminders.domain.entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Reminders.infrastructure.persistence.configuration;

public class ReminderDefinitionEntityConfiguration : IEntityTypeConfiguration<ReminderDefinition>
{
    public void Configure(EntityTypeBuilder<ReminderDefinition> b)
    {
        b.BaseEntityConfigure();

        b.Property(x => x.OwnerModule).HasMaxLength(200).IsRequired();
        b.Property(x => x.SubjectType).HasMaxLength(200).IsRequired();
        b.Property(x => x.SubjectId).HasMaxLength(200).IsRequired();
        b.Property(x => x.Kind).HasMaxLength(200).IsRequired();
        b.Property(x => x.TemplateKey).HasMaxLength(200).IsRequired();
        b.Property(x => x.RecipientResolverKey).HasMaxLength(200);
        b.Property(x => x.DigestKey).HasMaxLength(200);
        b.Property(x => x.Cron).HasMaxLength(120);

        b.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();

        b.EnumColumn(x => x.NotificationType);
        b.EnumColumn(x => x.ScheduleType);
        b.EnumColumn(x => x.IntervalPreset);
        b.EnumColumn(x => x.RecipientMode);
        b.EnumColumn(x => x.Status);
        b.EnumColumn(x => x.ChannelHint);

        // The idempotency-key contract guarantee — enforced by the DB, not a code convention. Non-filtered
        // (covers Cancelled/Completed rows) so re-registering a previously cancelled key resolves to the
        // same row and reactivates it in place (phase 02) rather than colliding.
        b.HasIndex(x => new { x.OwnerModule, x.SubjectType, x.SubjectId, x.Kind }).IsUnique();

        // Scanner index (phase 03): the scan seeks Status = Active AND NextOccurrenceAt <= now.
        b.HasIndex(x => new { x.Status, x.NextOccurrenceAt });
    }
}