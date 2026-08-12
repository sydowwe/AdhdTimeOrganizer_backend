using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Tracking.infrastructure.persistence.configuration.activityTracking.android;

public class AndroidSessionDataConfiguration : IEntityTypeConfiguration<AndroidSessionData>
{
    public void Configure(EntityTypeBuilder<AndroidSessionData> builder)
    {
        builder.BaseEntityConfigure();

        builder.Property(x => x.PackageName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.AppLabel).HasMaxLength(255).IsRequired();
        builder.Property(x => x.DeviceId).HasMaxLength(100).IsRequired();

        builder.IsManyWithOneUser();

        builder.HasIndex(x => new { x.UserId, x.DeviceId, x.PackageName, x.SessionStartUtc }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.SessionStartUtc });
        builder.HasIndex(x => new { x.UserId, x.PackageName });
    }
}