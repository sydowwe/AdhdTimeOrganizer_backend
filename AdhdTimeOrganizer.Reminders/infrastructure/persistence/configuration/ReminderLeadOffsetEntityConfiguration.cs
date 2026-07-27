using AdhdTimeOrganizer.Reminders.domain.entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Reminders.infrastructure.persistence.configuration;

public class ReminderLeadOffsetEntityConfiguration : IEntityTypeConfiguration<ReminderLeadOffset>
{
    public void Configure(EntityTypeBuilder<ReminderLeadOffset> b)
    {
        b.BaseEntityConfigure();

        b.Property(x => x.OffsetMinutes).IsRequired();

        // Cascade: lead offsets are owned by their one-shot definition.
        b.HasOne(x => x.ReminderDefinition)
            .WithMany(x => x.LeadOffsets)
            .HasForeignKey(x => x.ReminderDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}