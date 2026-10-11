using AdhdTimeOrganizer.Core.domain.model.entity.user;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.user;

public class UserEntityConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.BaseEntityConfigure();
        builder.EnumColumn(u => u.Locale);
        builder.EnumColumn(u => u.Theme);
        builder.Property(u => u.GoogleOAuthUserId).HasMaxLength(50);
        // Long-lived credential granting standing access to a third-party account, so it is encrypted
        // at rest (AES-256-GCM). Randomized ciphertext => cannot be filtered/sorted/uniqued; both call
        // sites (ConnectGoogleCalendarEndpoint, SyncCalendarToGoogleEndpoint) read it by user id only.
        // Rows written before this switch stay plaintext and decrypt untouched (see AesGcmFieldEncryptor);
        // they become encrypted on their next write.
        builder.EncryptedColumnNullable(u => u.GoogleCalendarRefreshToken);
        builder.Property(u => u.Timezone).IsRequired()
            .HasConversion(
                tz => tz.Id,
                id => TimeZoneInfo.FindSystemTimeZoneById(id));
        builder.Property(u => u.FirstDayOfWeek).HasDefaultValue(1).IsRequired();
        // Nullable and unindexed: it is read once per weather call, by user id, and "not set" is a first-class
        // state (no weather signal) rather than a missing value to be defaulted.
        builder.Property(u => u.WeatherLocation).HasMaxLength(120);
        builder.Property(u => u.AskBeforeDelete).HasDefaultValue(true).IsRequired();
        // The C# initializers are C#-only, like IsActive's below: without a database default the migration would
        // backfill every existing user with 00:00 for both. The sentinel is the default itself, so an insert carrying
        // 00:00 (a user who wakes at midnight) is written as 00:00 rather than mistaken for "unset" and replaced.
        builder.Property(u => u.DefaultWakeUpTime).HasDefaultValue(UserSleepWindow.Fallback.WakeUpTime)
            .HasSentinel(UserSleepWindow.Fallback.WakeUpTime).IsRequired();
        builder.Property(u => u.DefaultBedTime).HasDefaultValue(UserSleepWindow.Fallback.BedTime).IsRequired();
        builder.Ignore(u => u.SleepWindow);
        // BaseUser's `= true` initializer is C#-only; without this the migration backfills existing rows
        // with `false` and deactivates every user.
        builder.Property(u => u.IsActive).HasDefaultValue(true).IsRequired();
    }
}