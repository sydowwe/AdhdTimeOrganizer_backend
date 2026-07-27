using AdhdTimeOrganizer.Reminders.domain.entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Reminders.infrastructure.persistence.configuration;

public class ReminderRecipientEntityConfiguration : IEntityTypeConfiguration<ReminderRecipient>
{
    public void Configure(EntityTypeBuilder<ReminderRecipient> b)
    {
        b.BaseEntityConfigure();

        // Cascade: explicit recipients are owned by their definition — re-registering replaces the set,
        // and a deleted definition takes its recipients with it.
        b.HasOne(x => x.ReminderDefinition)
            .WithMany(x => x.Recipients)
            .HasForeignKey(x => x.ReminderDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Plain indexed user id — no FK to the user table (validated through the user query surface at
        // registration, not coupled here). Powers dispatch-time / dashboard lookups by recipient.
        b.HasIndex(x => x.UserId);
    }
}