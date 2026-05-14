using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.user;

public class UserEntityConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.BaseEntityConfigure();
        builder.EnumColumn(u => u.CurrentLocale);
        builder.EnumColumn(u => u.Theme);
        builder.Property(u => u.GoogleOAuthUserId).HasMaxLength(50);
        builder.Property(u => u.GoogleCalendarRefreshToken).HasMaxLength(500);
        builder.Property(u => u.Timezone).IsRequired()
            .HasConversion(
                tz => tz.Id,
                id => TimeZoneInfo.FindSystemTimeZoneById(id));
        builder.Property(u => u.FirstDayOfWeek).HasDefaultValue(1).IsRequired();
        builder.Property(u => u.AskBeforeDelete).HasDefaultValue(true).IsRequired();
        builder.Property(u => u.LastLoginAt);
    }
}