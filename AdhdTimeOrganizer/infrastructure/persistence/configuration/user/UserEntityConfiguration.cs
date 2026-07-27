using AdhdTimeOrganizer.domain.model.entity.user;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.user;

public class UserEntityConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.BaseEntityConfigure();
        builder.EnumColumn(u => u.Locale);
        builder.EnumColumn(u => u.Theme);
        builder.Property(u => u.GoogleOAuthUserId).HasMaxLength(50);
        builder.Property(u => u.GoogleCalendarRefreshToken).HasMaxLength(500);
        builder.Property(u => u.Timezone).IsRequired()
            .HasConversion(
                tz => tz.Id,
                id => TimeZoneInfo.FindSystemTimeZoneById(id));
        builder.Property(u => u.FirstDayOfWeek).HasDefaultValue(1).IsRequired();
        builder.Property(u => u.AskBeforeDelete).HasDefaultValue(true).IsRequired();
        // BaseUser's `= true` initializer is C#-only; without this the migration backfills existing rows
        // with `false` and deactivates every user.
        builder.Property(u => u.IsActive).HasDefaultValue(true).IsRequired();
    }
}