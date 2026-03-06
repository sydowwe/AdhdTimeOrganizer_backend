using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityTracking.desktop;

public class ActivityTrackingSettingsDesktopIgnoredProcessConfiguration : IEntityTypeConfiguration<ActivityTrackingSettingsDesktopIgnoredProcess>
{
    public void Configure(EntityTypeBuilder<ActivityTrackingSettingsDesktopIgnoredProcess> builder)
    {
        builder.BaseEntityConfigure();
        builder.Property(x => x.ProcessKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TitleContains).HasMaxLength(200);
        builder.Property(x => x.TitleContainsToggle).IsRequired();

        builder.IsManyWithOneUser(e => e.ActivityTrackingSettingsDesktopIgnoredProcessList);
    }
}
