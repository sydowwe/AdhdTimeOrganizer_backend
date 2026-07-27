using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MojaDigitalnaFirma.Core.Notifications.domain.entity;
using MojaDigitalnaFirma.Kernel.user;

namespace MojaDigitalnaFirma.Core.infrastructure.persistence.configuration;

public class PushSubscriptionCoreEntityConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> builder)
    {
        builder.HasOne<CoreUser>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}