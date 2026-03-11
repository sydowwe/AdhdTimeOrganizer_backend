using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityHistory;

public class WebExtensionActivityEntryConfiguration : IEntityTypeConfiguration<WebExtensionActivityEntry>
{
    public void Configure(EntityTypeBuilder<WebExtensionActivityEntry> builder)
    {
        builder.BaseEntityConfigure();

        // Override single-column PK from BaseEntityConfigure with composite PK
        builder.HasKey(x => new { x.Id, x.RecordDate });

        // Partitioned tables cannot return xmin in RETURNING clause
        builder.Property<uint>("row_version")
            .IsConcurrencyToken(false)
            .ValueGeneratedNever();

        builder.Property(x => x.Domain).HasMaxLength(255);
        builder.Property(x => x.Url).HasMaxLength(2048);

        builder.IsManyWithOneUser(u => u.WebExtensionActivityEntryList);

        builder.HasIndex(x => new { x.UserId, x.WindowStart, x.Domain, x.RecordDate }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.WindowStart });

        builder.IsPartitionedByRange("record_date",
        [
            new PartitionDefinition("web_extension_activity_entry_y2026", "2026-01-01", "2027-01-01"),
            new PartitionDefinition("web_extension_activity_entry_y2027", "2027-01-01", "2028-01-01"),
            new PartitionDefinition("web_extension_activity_entry_y2028", "2028-01-01", "2029-01-01"),
            new PartitionDefinition("web_extension_activity_entry_y2029", "2029-01-01", "2030-01-01"),
            new PartitionDefinition("web_extension_activity_entry_y2030", "2030-01-01", "2031-01-01"),
            new PartitionDefinition("web_extension_activity_entry_default", null, null)
        ]);
    }
}