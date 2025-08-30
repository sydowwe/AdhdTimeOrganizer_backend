using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence.converter;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityHistory;

public class ActivityHistoryConfiguration : IEntityTypeConfiguration<ActivityHistory>
{
    public void Configure(EntityTypeBuilder<ActivityHistory> builder)
    {
        builder.BaseEntityConfigure();

        builder.IsManyWithOneUser(u => u.ActivityHistoryList);
        builder.IsManyWithOneActivity(a=>a.ActivityHistoryList);

        builder.Property(a => a.StartTimestamp).IsRequired();
        builder.Property(a => a.EndTimestamp).IsRequired();
        builder.Property(h => h.Length)
            .HasConversion(new MyIntTimeConverter()).IsRequired();

        builder.HasIndex(a => new { a.UserId, a.ActivityId, a.StartTimestamp }).IsUnique();
    }
}