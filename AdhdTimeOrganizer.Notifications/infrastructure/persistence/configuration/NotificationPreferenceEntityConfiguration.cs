using AdhdTimeOrganizer.Notifications.domain.entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Notifications.infrastructure.persistence.configuration;

public class NotificationPreferenceEntityConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> b)
    {
        b.BaseEntityConfigure();
        // FK to the host's concrete user type is configured in AdhdTimeOrganizer, not here.
        b.EnumColumn(x => x.Type);
        b.EnumColumn(x => x.Channel);
        b.Property(x => x.Enabled).IsRequired();
        b.HasIndex(x => new { x.UserId, x.Type, x.Channel }).IsUnique();
    }
}