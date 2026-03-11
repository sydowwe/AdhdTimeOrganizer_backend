using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityTracking.desktop;

public class TrackerDesktopMappingByPatternConfiguration : IEntityTypeConfiguration<TrackerDesktopMappingByPattern>
{
    public void Configure(EntityTypeBuilder<TrackerDesktopMappingByPattern> builder)
    {
        builder.BaseEntityConfigure();
        builder.Property(x => x.ProcessName).HasMaxLength(200);
        builder.Property(x => x.ProductName).HasMaxLength(200);
        builder.Property(x => x.WindowTitle).HasMaxLength(255);

        builder.HasOne(e=>e.Activity).WithOne(a=>a.TrackerDesktopMappingByPattern)
            .HasForeignKey<TrackerDesktopMappingByPattern>(e=>e.ActivityId);

        builder.HasOne(e=>e.Role).WithMany(e=>e.TrackerDesktopMappingByPatternList)
            .HasForeignKey(e=>e.RoleId);

        builder.HasOne(e=>e.Category).WithMany(e=>e.TrackerDesktopMappingByPatternList)
            .HasForeignKey(e=>e.CategoryId);

        builder.IsManyWithOneUser(e => e.TrackerDesktopMappingByPatternList);

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
