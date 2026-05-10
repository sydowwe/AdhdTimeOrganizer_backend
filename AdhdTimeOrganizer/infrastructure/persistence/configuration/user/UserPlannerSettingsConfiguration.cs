using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.user;

public class UserPlannerSettingsConfiguration : IEntityTypeConfiguration<UserPlannerSettings>
{
    public void Configure(EntityTypeBuilder<UserPlannerSettings> builder)
    {
        builder.BaseEntityConfigure();
        builder.IsOneWithOneUser<UserPlannerSettings>(u => u.PlannerSettings);

        builder.Property(s => s.RemindersEnabled).IsRequired().HasDefaultValue(true);
        builder.Property(s => s.ReminderMinutesBefore).IsRequired().HasDefaultValue(10);
        builder.Property(s => s.DetailsPanelExpandedByDefault).IsRequired().HasDefaultValue(true);
        builder.Property(s => s.ArrowKeyNavEnabled).IsRequired().HasDefaultValue(true);

        builder.Property(s => s.PredefinedSkipReasons)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(s => s.SlotDurationMinutes).IsRequired().HasDefaultValue(10);
        builder.Property(s => s.DefaultApplyTemplateId);
        builder.EnumColumn(s => s.DefaultConflictResolution);
        builder.Property(s => s.DefaultApplyPreviewMode).IsRequired().HasDefaultValue(true);

        builder.HasOne(s => s.DefaultApplyTemplate)
            .WithMany()
            .HasForeignKey(s => s.DefaultApplyTemplateId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
