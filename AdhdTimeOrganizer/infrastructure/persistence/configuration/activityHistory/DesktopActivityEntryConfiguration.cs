using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.extensions;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityHistory;

public class DesktopActivityEntryConfiguration : IEntityTypeConfiguration<DesktopActivityEntry>
{
    public void Configure(EntityTypeBuilder<DesktopActivityEntry> builder)
    {
        builder.BaseEntityConfigure();

        // Override single-column PK from BaseEntityConfigure with composite PK
        builder.HasKey(x => new { x.Id, x.RecordDate });

        // Partitioned tables cannot return xmin in RETURNING clause
        builder.Property<uint>("row_version")
            .IsConcurrencyToken(false)
            .ValueGeneratedNever();

        builder.Property(x => x.ProductName).HasMaxLength(255);
        builder.Property(x => x.ProcessName).HasMaxLength(255);
        // WindowTitle stays plaintext deliberately — FetchTableDistinctDesktopEntry does a SQL-side
        // GROUP BY on it plus an Exact/Contains/Wildcard/Regex filter and a sort, none of which
        // survive randomized encryption. A hash column would recover grouping and Exact only, not
        // the other three match modes. See SEC-4 in review/portal/02-findings.md.
        builder.Property(x => x.WindowTitle).HasMaxLength(1024);

        // ExecutablePath is write-only: set at ingest, never filtered, grouped, indexed or projected.
        // Full filesystem paths are high-sensitivity PII (usernames, project and document names), so
        // it is encrypted at rest (GDPR Art. 32). Nothing reads it by value, so randomized
        // encryption costs nothing here.
        builder.EncryptedColumn(x => x.ExecutablePath);

        builder.IsManyWithOneUser();

        builder.HasIndex(x => new { x.UserId, x.WindowStart, x.RecordDate, x.ProcessName, x.WindowTitle }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.WindowStart });

        builder.IsPartitionedByRange("record_date",
        [
            new PartitionDefinition("desktop_activity_entry_y2026", "2026-01-01", "2027-01-01"),
            new PartitionDefinition("desktop_activity_entry_y2027", "2027-01-01", "2028-01-01"),
            new PartitionDefinition("desktop_activity_entry_y2028", "2028-01-01", "2029-01-01"),
            new PartitionDefinition("desktop_activity_entry_y2029", "2029-01-01", "2030-01-01"),
            new PartitionDefinition("desktop_activity_entry_y2030", "2030-01-01", "2031-01-01"),
            new PartitionDefinition("desktop_activity_entry_default", null, null)
        ]);
    }
}