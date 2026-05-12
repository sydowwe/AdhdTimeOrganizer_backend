using AdhdTimeOrganizer.domain.model.entity.suggestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.suggestion;

public class ActivityHistoryPatternConfiguration : IEntityTypeConfiguration<ActivityHistoryPattern>
{
    public void Configure(EntityTypeBuilder<ActivityHistoryPattern> builder)
    {
        builder.HasNoKey();
        builder.ToView("mv_activity_history_pattern");

        builder.HasOne(p => p.Activity)
            .WithMany()
            .HasForeignKey(p => p.ActivityId);
    }
}
