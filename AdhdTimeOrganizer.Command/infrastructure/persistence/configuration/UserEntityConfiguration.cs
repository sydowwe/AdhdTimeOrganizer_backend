using AdhdTimeOrganizer.Command.domain.model.entity.user;
using AdhdTimeOrganizer.Common.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.Command.infrastructure.persistence.configuration;

public class UserEntityConfiguration : IEntityTypeConfiguration<UserEntity>
{
    public void Configure(EntityTypeBuilder<UserEntity> builder)
    {
        builder.EnumColumn(u => u.CurrentLocale).IsRequired();
        builder.Property(u => u.GoogleOAuthUserId).HasMaxLength(50);
        builder.Property(u => u.Timezone).IsRequired()
            .HasConversion(
                tz => tz.Id,
                id => TimeZoneInfo.FindSystemTimeZoneById(id));
    }
}