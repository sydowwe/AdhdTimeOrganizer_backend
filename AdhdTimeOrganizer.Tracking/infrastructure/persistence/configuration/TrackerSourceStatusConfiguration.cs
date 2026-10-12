using AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.extensions;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Tracking.infrastructure.persistence.configuration.activityTracking;

public class TrackerSourceStatusConfiguration : IEntityTypeConfiguration<TrackerSourceStatus>
{
    public void Configure(EntityTypeBuilder<TrackerSourceStatus> builder)
    {
        builder.BaseEntityConfigure();

        builder.EnumColumn(x => x.Source).HasMaxLength(20);

        builder.IsManyWithOneUser();

        // One row per tracker kind: the stamp's insert relies on this to lose a first-report race harmlessly.
        builder.HasIndex(x => new { x.UserId, x.Source }).IsUnique();
    }
}
