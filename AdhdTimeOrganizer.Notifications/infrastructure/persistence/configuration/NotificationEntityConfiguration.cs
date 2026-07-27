using AdhdTimeOrganizer.Notifications.domain.entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Notifications.infrastructure.persistence.configuration;

public class NotificationEntityConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.BaseEntityConfigure();
        b.EnumColumn(x => x.Type);
        b.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.IsRead).IsRequired();

        // UserId is the recipient (manual, not auto-stamped). The FK to the host's concrete user type is
        // configured in AdhdTimeOrganizer, not here — this module does not know that type.
        b.HasIndex(x => new { x.UserId, x.CreatedTimestamp })
            .IsDescending(false, true);

        // The flush job's only predicate (DeferredUntil <= now). Filtered, because the column is null on
        // effectively every row — a partial index keeps the sweep a tiny range seek instead of a scan over
        // the whole history table, and costs nothing on the insert-heavy normal path.
        b.HasIndex(x => x.DeferredUntil)
            .HasFilter("deferred_until IS NOT NULL");
    }
}