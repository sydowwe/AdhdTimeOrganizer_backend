using AdhdTimeOrganizer.domain.model.entity.user;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.user;

public class UserEntityConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.BaseEntityConfigure();
        builder.EnumColumn(u => u.CurrentLocale);
        builder.Property(u => u.GoogleOAuthUserId).HasMaxLength(50);
        builder.Property(u => u.Timezone).IsRequired()
            .HasConversion(
                tz => tz.Id,
                id => TimeZoneInfo.FindSystemTimeZoneById(id));
    }
}