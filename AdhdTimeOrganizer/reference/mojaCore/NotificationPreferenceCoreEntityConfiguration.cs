using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MojaDigitalnaFirma.Core.Notifications.domain.entity;
using MojaDigitalnaFirma.Kernel.user;

namespace MojaDigitalnaFirma.Core.infrastructure.persistence.configuration;

public class NotificationPreferenceCoreEntityConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.HasOne<CoreUser>()
            .WithMany()
            .HasForeignKey(np => np.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}