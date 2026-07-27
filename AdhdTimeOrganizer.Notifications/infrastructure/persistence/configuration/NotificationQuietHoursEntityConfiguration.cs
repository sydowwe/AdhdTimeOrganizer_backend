using AdhdTimeOrganizer.Notifications.domain.entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Notifications.infrastructure.persistence.configuration;

public class NotificationQuietHoursEntityConfiguration : IEntityTypeConfiguration<NotificationQuietHours>
{
    public void Configure(EntityTypeBuilder<NotificationQuietHours> b)
    {
        b.BaseEntityConfigure();
        // No FK to the user table on purpose — see the entity's remarks.
        b.Property(x => x.StartMinute).IsRequired();
        b.Property(x => x.EndMinute).IsRequired();

        // At most one quiet-hours window per user.
        b.HasIndex(x => x.UserId).IsUnique();
    }
}