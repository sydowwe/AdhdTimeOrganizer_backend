using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;
using Sydowwe.Framework.infrastructure.persistence.converter;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityHistory;

public class ActivityHistoryConfiguration : IEntityTypeConfiguration<ActivityHistory>
{
    public void Configure(EntityTypeBuilder<ActivityHistory> builder)
    {
        builder.BaseEntityConfigure();

        builder.IsManyWithOneUser(u => u.ActivityHistoryList);
        builder.IsManyWithOneActivity(a => a.ActivityHistoryList);

        builder.Property(a => a.StartTimestamp).IsRequired();
        builder.Property(a => a.EndTimestamp).IsRequired();
        builder.Property(h => h.Length)
            .HasConversion(new IntTimeConverter()).IsRequired();

        builder.HasIndex(a => new { a.UserId, a.ActivityId, a.StartTimestamp }).IsUnique();
    }
}