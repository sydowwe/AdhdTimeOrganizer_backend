using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityHistory;

public class AlarmConfiguration : IEntityTypeConfiguration<Alarm>
{
    public void Configure(EntityTypeBuilder<Alarm> builder)
    {
        builder.BaseEntityConfigure();

        builder.IsManyWithOneUser(u => u.AlarmList);
        builder.IsManyWithOneActivity(a=>a.AlarmList);

        builder.Property(a => a.StartTimestamp).IsRequired();
        builder.Property(a => a.IsActive).HasDefaultValue(true).IsRequired();

        // Define indexing if needed
        builder.HasIndex(a => new { a.UserId, a.StartTimestamp }).IsUnique();
    }
}