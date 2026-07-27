using AdhdTimeOrganizer.Notifications.domain.entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Notifications.infrastructure.persistence.configuration;

public class PushSubscriptionEntityConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> b)
    {
        b.BaseEntityConfigure();
        // FK to the host's concrete user type is configured in AdhdTimeOrganizer, not here.
        b.Property(x => x.Endpoint).IsRequired();
        b.Property(x => x.P256dh).IsRequired();
        b.Property(x => x.Auth).IsRequired();
        b.Property(x => x.UserAgent).HasMaxLength(400);
        b.HasIndex(x => x.Endpoint).IsUnique();
    }
}