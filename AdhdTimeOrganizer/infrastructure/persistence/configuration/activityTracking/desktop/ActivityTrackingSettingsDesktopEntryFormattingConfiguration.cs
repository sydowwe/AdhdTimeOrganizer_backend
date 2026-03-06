using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityTracking.desktop;

public class ActivityTrackingSettingsDesktopEntryFormattingConfiguration : IEntityTypeConfiguration<ActivityTrackingSettingsDesktopEntryFormatting>
{
    public void Configure(EntityTypeBuilder<ActivityTrackingSettingsDesktopEntryFormatting> builder)
    {
        builder.BaseEntityConfigure();
        builder.Property(x => x.ProcessKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ProcessNice).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TitleSplit).HasMaxLength(200);
        builder.Property(x => x.IsSavedToMainHistory).IsRequired();

        builder.IsManyWithOneUser(e => e.ActivityTrackingSettingsDesktopEntryFormattingList);
    }
}
