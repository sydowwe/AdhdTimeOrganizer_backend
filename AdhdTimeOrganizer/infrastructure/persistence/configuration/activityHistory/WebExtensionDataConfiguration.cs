using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityHistory;

public class WebExtensionDataConfiguration : IEntityTypeConfiguration<WebExtensionData>
{
    public void Configure(EntityTypeBuilder<WebExtensionData> builder)
    {
        builder.BaseEntityConfigure();

        builder.IsManyWithOneUser(u => u.WebExtensionDataList);

        builder.Property(x => x.Domain).HasMaxLength(255);
        builder.Property(x => x.Url).HasMaxLength(2048);

        builder.HasIndex(x => new { x.UserId, x.WindowStart, x.Domain }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.WindowStart });
    }
}