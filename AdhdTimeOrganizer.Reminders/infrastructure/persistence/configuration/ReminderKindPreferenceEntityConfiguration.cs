using AdhdTimeOrganizer.Reminders.domain.entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Reminders.infrastructure.persistence.configuration;

public class ReminderKindPreferenceEntityConfiguration : IEntityTypeConfiguration<ReminderKindPreference>
{
    public void Configure(EntityTypeBuilder<ReminderKindPreference> b)
    {
        b.BaseEntityConfigure();

        b.EnumColumn(x => x.ChannelHint);
        b.Property(x => x.Enabled).IsRequired();

        // One opt-out row per (user, owner module, kind). Mirrors NotificationPreference's unique key shape;
        // the leading UserId also serves the scanner's "opted out of this kind?" lookup and the self-scoped read.
        b.HasIndex(x => new { x.UserId, x.OwnerModule, x.Kind }).IsUnique();
    }
}