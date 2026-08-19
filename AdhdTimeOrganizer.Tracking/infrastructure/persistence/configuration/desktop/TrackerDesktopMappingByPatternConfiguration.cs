using AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.extensions;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Tracking.infrastructure.persistence.configuration.activityTracking.desktop;

public class TrackerDesktopMappingByPatternConfiguration : IEntityTypeConfiguration<TrackerDesktopMappingByPattern>
{
    public void Configure(EntityTypeBuilder<TrackerDesktopMappingByPattern> builder)
    {
        builder.BaseEntityConfigure();
        builder.Property(x => x.ProcessName).HasMaxLength(200);
        builder.Property(x => x.ProductName).HasMaxLength(200);
        builder.Property(x => x.WindowTitle).HasMaxLength(255);

        // All four relationships are configured from this (dependent) side with no inverse navigation:
        // Activity, ActivityRole, ActivityCategory and User deliberately do not name tracking types.
        // The parameterless WithMany() creates no inverse navigation either — it is WithOne() that
        // would make this a 1:1, and the domain key here is the pattern, not the activity: a user maps
        // both code.exe and rider64.exe onto "Programming".
        builder.HasOne(e => e.Activity).WithMany()
            .HasForeignKey(e => e.ActivityId)
            .OnDelete(DeleteBehavior.Cascade)
            // Pinned, for the same reason as the three Activity*Profile configurations — see the
            // comment on ActivityBacklogProfileConfiguration. This is the name the database already
            // carries; leaving it derived means any later reshape of the relationship emits a silent
            // DROP + ADD CONSTRAINT pair with an ACCESS EXCLUSIVE lock and a full revalidation.
            .HasConstraintName("fk_tracker_desktop_mapping_by_pattern_activities_activity_id");
        // Non-unique: TrackerMappingActivityReferenceSource.ReferencingActivityIds scans this column
        // on every activity-grid page.
        builder.HasIndex(e => e.ActivityId);

        builder.HasOne(e => e.Role).WithMany()
            .HasForeignKey(e => e.RoleId);

        builder.HasOne(e => e.Category).WithMany()
            .HasForeignKey(e => e.CategoryId);

        builder.IsManyWithOneUser();

        // Unique pattern per user — same (ProcessName, ProductName, WindowTitle) combination cannot
        // appear twice. NULLs are treated as equal (NULLS NOT DISTINCT) so two NULL ProcessNames
        // are considered the same value for uniqueness purposes.
        builder.HasIndex(e => new { e.UserId, e.ProcessName, e.ProductName, e.WindowTitle })
            .IsUnique()
            .HasAnnotation("Npgsql:NullsDistinct", false);

        // Exactly one target group must be set:
        //   group 1: IsIgnored = true
        //   group 2: ActivityId not null
        //   group 3: RoleId or CategoryId not null (both allowed together)
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_TrackerDesktopMappingByPattern_TargetRequired",
            """
            (
              CASE WHEN "is_ignored" = TRUE THEN 1 ELSE 0 END +
              CASE WHEN "activity_id" IS NOT NULL THEN 1 ELSE 0 END +
              CASE WHEN "role_id" IS NOT NULL OR "category_id" IS NOT NULL THEN 1 ELSE 0 END
            ) = 1
            """
        ));

        // IsIgnored can only ever be true, never false
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_TrackerDesktopMappingByPattern_IsIgnoredOnlyTrue",
            "\"is_ignored\" IS NULL OR \"is_ignored\" = TRUE"
        ));

        // ProcessName => ProcessNameMatchType must not be null
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_TrackerDesktopMappingByPattern_ProcessNameMatchType",
            "process_name IS NULL OR process_name_match_type IS NOT NULL"
        ));

        // ProductName => ProductNameMatchType must not be null
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_TrackerDesktopMappingByPattern_ProductNameMatchType",
            "product_name IS NULL OR product_name_match_type IS NOT NULL"
        ));

        // WindowTitle => WindowTitleMatchType must not be null
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_TrackerDesktopMappingByPattern_WindowTitleMatchType",
            "window_title IS NULL OR window_title_match_type IS NOT NULL"
        ));
    }
}