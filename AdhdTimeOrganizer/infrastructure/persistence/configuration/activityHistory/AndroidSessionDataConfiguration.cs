using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.domain.model.entity.activityTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityHistory;

public class AndroidSessionDataConfiguration : IEntityTypeConfiguration<AndroidSessionData>
{
    public void Configure(EntityTypeBuilder<AndroidSessionData> builder)
    {
        builder.ToTable("android_session_data");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()").IsRequired();

        builder.HasIndex(x => new { x.UserId, x.DeviceId, x.PackageName, x.SessionStartUtc }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.SessionStartUtc });
        builder.HasIndex(x => new { x.UserId, x.PackageName });

        builder.Property(x => x.PackageName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.AppLabel).HasMaxLength(255).IsRequired();
        builder.Property(x => x.DeviceId).HasMaxLength(100).IsRequired();

        builder.HasOne(x => x.User)
            .WithMany(u => u.AndroidSessionDataList)
            .HasForeignKey(x => x.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
