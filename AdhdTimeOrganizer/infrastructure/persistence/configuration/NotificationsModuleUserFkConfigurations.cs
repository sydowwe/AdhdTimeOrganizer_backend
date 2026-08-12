using AdhdTimeOrganizer.Core.domain.model.entity.user;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Notifications.domain.entity;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.module;

// The Notifications module keys its rows by a plain `long UserId` and has no navigation to a user type —
// it is not allowed to know one. Binding those columns to the concrete User is the composition root's job,
// which is this file. Same seam the source solution used (see reference/mojaCore/README.md).
//
// No inverse navigation is declared (`WithMany()` with no argument): User must not grow collections of
// module rows, or the app project would gain a dependency on the modules' entity graph.
//
// Only these three get an FK. NotificationQuietHours and every Reminders entity document themselves as
// deliberately FK-free ("a plain indexed column with no FK to the user table") — recipient ids there are
// validated at registration instead, so do not add configurations for them.

public class NotificationUserFkConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class NotificationPreferenceUserFkConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PushSubscriptionUserFkConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> builder)
    {
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}