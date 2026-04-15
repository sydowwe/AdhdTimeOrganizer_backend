using AdhdTimeOrganizer.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityHistory;

public class AndroidSessionDataConfiguration : IEntityTypeConfiguration<AndroidSessionData>
{
    public void Configure(EntityTypeBuilder<AndroidSessionData> builder)
    {
        builder.BaseEntityConfigure();

        builder.Property(x => x.PackageName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.AppLabel).HasMaxLength(255).IsRequired();
        builder.Property(x => x.DeviceId).HasMaxLength(100).IsRequired();

        builder.IsManyWithOneUser(u => u.AndroidSessionDataList);

        builder.HasIndex(x => new { x.UserId, x.DeviceId, x.PackageName, x.SessionStartUtc }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.SessionStartUtc });
        builder.HasIndex(x => new { x.UserId, x.PackageName });
    }
}
