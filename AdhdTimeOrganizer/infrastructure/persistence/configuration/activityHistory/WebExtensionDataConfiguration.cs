using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityHistory;

public class WebExtensionDataConfiguration : IEntityTypeConfiguration<WebExtensionData>
{
    public void Configure(EntityTypeBuilder<WebExtensionData> builder)
    {
        builder.BaseEntityConfigure();

        builder.IsManyWithOneUser(u => u.WebExtensionDataList);
        builder.IsManyWithOneActivity(a=>a.WebExtensionDataList);

        builder.Property(a => a.Domain).IsRequired();
        builder.Property(a => a.Title).IsRequired();
        builder.Property(a => a.Duration).IsRequired();
        builder.Property(a => a.StartTimestamp).IsRequired();


        builder.HasIndex(a => new { a.UserId, a.ActivityId, a.StartTimestamp }).IsUnique();
    }
}