using AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.extensions;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Planning.infrastructure.persistence.configuration.activityPlanning;

public class UserPlannerSettingsConfiguration : IEntityTypeConfiguration<UserPlannerSettings>
{
    public void Configure(EntityTypeBuilder<UserPlannerSettings> builder)
    {
        builder.BaseEntityConfigure();
        builder.IsOneWithOneUser<UserPlannerSettings>();

        // Stated rather than inherited. The helper's HasIndex(UserId) is non-unique; what makes
        // ix_user_planner_settings_user_id unique today is EF's foreign-key index convention reacting to the
        // 1:1 relationship declared on the next line of the helper — a uniqueness nobody wrote down and that a
        // future edit to the relationship would silently drop, taking the one-row-per-user invariant with it.
        // Declaring it here is the same index (no migration), now explicitly configured.
        builder.HasIndex(s => s.UserId).IsUnique();

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