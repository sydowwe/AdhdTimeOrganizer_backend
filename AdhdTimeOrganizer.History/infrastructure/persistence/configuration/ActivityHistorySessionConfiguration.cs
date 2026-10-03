using AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.extensions;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.History.infrastructure.persistence.configuration.activityHistory;

public class ActivityHistorySessionConfiguration : IEntityTypeConfiguration<ActivityHistorySession>
{
    public void Configure(EntityTypeBuilder<ActivityHistorySession> builder)
    {
        builder.BaseEntityConfigure();

        builder.IsManyWithOneUser();

        builder.EnumColumn(s => s.Kind).HasMaxLength(20);
        builder.EnumColumn(s => s.Outcome).HasMaxLength(20);

        // The row → session FK is declared on ActivityHistoryConfiguration, the dependent's side, where its
        // delete behaviour is explained.
    }
}
