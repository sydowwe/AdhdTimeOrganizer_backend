using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.android;
using AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Tracking.infrastructure.persistence.configuration.activityTracking.android;

public class TrackerAndroidMappingByPatternConfiguration : IEntityTypeConfiguration<TrackerAndroidMappingByPattern>
{
    public void Configure(EntityTypeBuilder<TrackerAndroidMappingByPattern> builder)
    {
        builder.BaseEntityConfigure();
        builder.Property(x => x.PackageName).HasMaxLength(255);
        builder.Property(x => x.AppLabel).HasMaxLength(255);

        // All four relationships are configured from this (dependent) side with no inverse navigation:
        // Activity, ActivityRole, ActivityCategory and User deliberately do not name tracking types.
        builder.HasOne(e => e.Activity).WithOne()
            .HasForeignKey<TrackerAndroidMappingByPattern>(e => e.ActivityId);

        builder.HasOne(e => e.Role).WithMany()
            .HasForeignKey(e => e.RoleId);

        builder.HasOne(e => e.Category).WithMany()
            .HasForeignKey(e => e.CategoryId);

        builder.IsManyWithOneUser();

        builder.HasIndex(e => new { e.UserId, e.PackageName, e.AppLabel })
            .IsUnique()
            .HasAnnotation("Npgsql:NullsDistinct", false);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_TrackerAndroidMappingByPattern_TargetRequired",
            """
            (
              CASE WHEN "is_ignored" = TRUE THEN 1 ELSE 0 END +
              CASE WHEN "activity_id" IS NOT NULL THEN 1 ELSE 0 END +
              CASE WHEN "role_id" IS NOT NULL OR "category_id" IS NOT NULL THEN 1 ELSE 0 END
            ) = 1
            """
        ));

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_TrackerAndroidMappingByPattern_IsIgnoredOnlyTrue",
            "\"is_ignored\" IS NULL OR \"is_ignored\" = TRUE"
        ));

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_TrackerAndroidMappingByPattern_PackageNameMatchType",
            "package_name IS NULL OR package_name_match_type IS NOT NULL"
        ));

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_TrackerAndroidMappingByPattern_AppLabelMatchType",
            "app_label IS NULL OR app_label_match_type IS NOT NULL"
        ));
    }
}